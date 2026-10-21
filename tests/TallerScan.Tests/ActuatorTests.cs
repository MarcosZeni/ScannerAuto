using TallerScan.Core.Actuators;
using TallerScan.Core.Models;
using TallerScan.Core.Safety;
using TallerScan.Infrastructure.Actuators;
using TallerScan.Infrastructure.Elm;
using TallerScan.Infrastructure.Sessions;
using TallerScan.Infrastructure.Simulation;
using TallerScan.Infrastructure.Transports;
using TallerScan.Infrastructure.Uds;

namespace TallerScan.Tests;

// Borrado de fallas y pruebas de actuadores: alcances de escritura y flujo contra el simulador.
public class ActuatorTests
{
    private static readonly ActuatorProfile Sim = SimulatedActuators.Profile;

    private static async Task<DiagnosticSession> ConnectAsync(string scenario, EngineMode mode)
    {
        var session = new DiagnosticSession(new TransportFactory(), new TraceLog());
        await session.ConnectAsync(new SimulatedEndpoint(scenario), CancellationToken.None);
        Assert.Equal(SessionState.Ready, session.State);
        session.Simulator!.Engine.Mode = mode;
        return session;
    }

    private static ActuatorTest Test(string id) => Sim.Tests.Single(t => t.Id == id);

    [Fact]
    public void Alcances_de_escritura()
    {
        Assert.False(CommandPolicy.IsAllowed("04", CommandScope.ReadOnly, out var why));
        Assert.Contains("confirmación", why);
        Assert.True(CommandPolicy.IsAllowed("04", CommandScope.ClearDtc, out _));
        Assert.False(CommandPolicy.IsAllowed("2F40010302", CommandScope.ClearDtc, out _));

        var scope = UdsClient.ScopeFor(Sim, "prueba");
        Assert.True(CommandPolicy.IsAllowed("2F40010302", scope, out _));   // electro en alta
        Assert.True(CommandPolicy.IsAllowed("2F400100", scope, out _));     // devolver control
        Assert.True(CommandPolicy.IsAllowed("ATSH7E0", scope, out _));
        Assert.True(CommandPolicy.IsAllowed("ATCRA7E8", scope, out _));
        Assert.True(CommandPolicy.IsAllowed("1003", scope, out _));
        Assert.True(CommandPolicy.IsAllowed("14FFFFFF", scope, out _));
        Assert.False(CommandPolicy.IsAllowed("2F40010305", scope, out _));  // valor no previsto
        Assert.False(CommandPolicy.IsAllowed("2F99990301", scope, out _));  // DID desconocido
        Assert.False(CommandPolicy.IsAllowed("ATSH7E5", scope, out _));     // otro módulo
        Assert.False(CommandPolicy.IsAllowed("2701", scope, out _));        // seguridad
        Assert.False(CommandPolicy.IsAllowed("ATSH7E0", CommandScope.ReadOnly, out _));
    }

    [Fact]
    public async Task Borrado_generico_y_por_modulo()
    {
        await using var s = await ConnectAsync("multi-ecu", EngineMode.Off);
        var before = await s.ScanDtcsAsync(CancellationToken.None);
        Assert.Equal(["P0700", "P0715"], before.Stored.SelectMany(r => r.Codes).Order().ToArray());

        var result = await s.ClearDtcsAsync(CancellationToken.None);
        Assert.True(result.AnyAccepted);
        Assert.Equal("7E8", result.Modules.Single(m => m.Status == ReadStatus.Success).Origin.Address);
        // La caja ignora el 04: su falla sigue ahí y el resultado lo dice.
        Assert.Contains(result.CodesAfter, c => c.StartsWith("P0715"));
        Assert.DoesNotContain(result.CodesAfter, c => c.StartsWith("P0700"));
        Assert.Single(s.Actions);
        Assert.True(s.Actions[0].Ok);

        var after = await s.ClearModuleAsync(Sim.ModuleClears.Single(), CancellationToken.None);
        Assert.Empty(after.Stored.SelectMany(r => r.Codes));
        // Al terminar se volvió al pedido funcional: responden los dos módulos.
        Assert.Equal(2, after.Stored.Count);
        Assert.Null(s.Actuators);

        // Lo hecho queda en el historial y en el informe.
        var record = Infrastructure.History.SessionRecordBuilder.From(s, [], "AB 123 CD", "", "");
        Assert.Equal(2, record.Actions.Count);
        var html = Infrastructure.Reporting.HtmlReport.Build(record);
        Assert.Contains("Acciones realizadas", html);
        Assert.Contains("Borrado de fallas (modo 04)", html);
    }

    [Fact]
    public async Task Borrado_con_motor_en_marcha_es_rechazado()
    {
        await using var s = await ConnectAsync("falla-encendido", EngineMode.Idle);
        var result = await s.ClearDtcsAsync(CancellationToken.None);
        Assert.False(result.AnyAccepted);
        Assert.Contains("Condiciones", result.Modules.Single().Message);
        Assert.Equal(result.CodesBefore, result.CodesAfter);
    }

    [Fact]
    public async Task Electro_en_alta_y_apagado()
    {
        await using var s = await ConnectAsync("sano", EngineMode.Off);
        var engine = s.Simulator!.Engine;
        var runner = s.StartActuators();
        var fan = Test("electro");

        var alta = await runner.RunStepAsync(fan, fan.Steps.Single(x => x.Name == "Alta"), CancellationToken.None);
        Assert.True(alta.Ok, alta.Message);
        Assert.Equal(2, engine.Actuator(SimulatedActuators.Fan));
        Assert.Equal("02", await runner.ReadStatusAsync(fan, CancellationToken.None));
        var feedback = await runner.ReadFeedbackAsync(fan, CancellationToken.None);
        Assert.NotNull(feedback?.Value);
        Assert.Equal("°C", feedback!.Value.Unit);

        await runner.StopAsync(CancellationToken.None);
        Assert.Equal(0, engine.Actuator(SimulatedActuators.Fan));
        Assert.Null(runner.ActiveTest);

        await s.EndActuatorsAsync();
        // Sin sesión de actuadores el link vuelve a solo lectura y al pedido funcional.
        var scan = await s.ScanDtcsAsync(CancellationToken.None);
        Assert.Equal(ReadStatus.Success, scan.Stored.Single().Status);
        Assert.Contains(s.Actions, a => a.Kind == "Electroventilador" && a.Ok);
    }

    [Fact]
    public async Task Prueba_con_motor_en_marcha_no_se_manda()
    {
        await using var s = await ConnectAsync("sano", EngineMode.Idle);
        var runner = s.StartActuators();
        var fan = Test("electro");
        var e = await runner.RunStepAsync(fan, fan.Steps[0], CancellationToken.None);
        Assert.False(e.Ok);
        Assert.Contains("en marcha", e.Message);
        Assert.Equal(0, s.Simulator!.Engine.Actuator(SimulatedActuators.Fan));
        Assert.DoesNotContain(s.Trace.Snapshot(), t => t.Command.StartsWith("2F"));
    }

    [Fact]
    public async Task Cambiar_de_prueba_apaga_la_anterior()
    {
        await using var s = await ConnectAsync("sano", EngineMode.Off);
        var engine = s.Simulator!.Engine;
        var runner = s.StartActuators();
        var fan = Test("electro");
        var pump = Test("bomba");
        await runner.RunStepAsync(fan, fan.Steps[0], CancellationToken.None);
        Assert.Equal(1, engine.Actuator(SimulatedActuators.Fan));
        var e = await runner.RunStepAsync(pump, pump.Steps[0], CancellationToken.None);
        Assert.True(e.Ok, e.Message);
        Assert.Equal(0, engine.Actuator(SimulatedActuators.Fan));
        Assert.Equal(1, engine.Actuator(SimulatedActuators.FuelPump));
    }

    [Fact]
    public async Task Se_apaga_sola_al_vencer_el_tiempo()
    {
        await using var s = await ConnectAsync("sano", EngineMode.Off);
        var engine = s.Simulator!.Engine;
        var fan = Test("electro") with { MaxDuration = TimeSpan.FromMilliseconds(500) };
        var profile = Sim with { Tests = [fan] };
        await using var runner = new ActuatorRunner(s.Simulator is null ? throw new InvalidOperationException() : GetBackend(s), profile);
        await runner.RunStepAsync(fan, fan.Steps[1], CancellationToken.None);
        Assert.Equal(2, engine.Actuator(SimulatedActuators.Fan));
        await Task.Delay(TimeSpan.FromSeconds(4));
        Assert.Equal(0, engine.Actuator(SimulatedActuators.Fan));
        Assert.Null(runner.ActiveTest);
        Assert.Contains(runner.Events, e => e.Step == "Tiempo");
    }

    [Fact]
    public async Task Desconectar_apaga_lo_forzado()
    {
        await using var s = await ConnectAsync("sano", EngineMode.Off);
        var engine = s.Simulator!.Engine;
        var runner = s.StartActuators();
        var fan = Test("electro");
        await runner.RunStepAsync(fan, fan.Steps[1], CancellationToken.None);
        Assert.Equal(2, engine.Actuator(SimulatedActuators.Fan));
        await s.DisconnectAsync();
        Assert.Equal(0, engine.Actuator(SimulatedActuators.Fan));
        Assert.Null(s.Actuators);
    }

    [Fact]
    public async Task Respuesta_negativa_explicada()
    {
        var emulator = new Elm327Emulator(Scenarios.Create("sano"));
        await using var link = new ElmLink(new LoopbackTransport(emulator), new TraceLog());
        await link.OpenAsync(CancellationToken.None);
        await link.SendAsync("ATE0", TimeSpan.FromSeconds(2), CancellationToken.None);
        await link.SendAsync("ATH1", TimeSpan.FromSeconds(2), CancellationToken.None);
        link.Scope = new CommandScope("test", ["ATSH7E0", "ATCRA7E8", "1003", "22FFFF", "3E80", "ATAR", "ATSH7DF"]);
        var uds = new UdsClient(link, new ObdProtocol('6', "CAN"));
        Assert.True(await uds.AddressAsync(new ModuleAddress("Motor", "7E0", "7E8"), CancellationToken.None));
        var open = await uds.RequestAsync("1003", CancellationToken.None);
        Assert.True(open.Ok);
        var bad = await uds.RequestAsync("22FFFF", CancellationToken.None);
        Assert.False(bad.Ok);
        Assert.Equal((byte)0x31, bad.Nrc);
        Assert.Contains("fuera de rango", bad.Message);
        var tp = await uds.RequestAsync("3E80", CancellationToken.None, expectNoResponse: true);
        Assert.True(tp.Ok);
        var blocked = await uds.RequestAsync("2F40010301", CancellationToken.None);
        Assert.Equal(ReadStatus.Blocked, blocked.Status);
    }

    [Fact]
    public void Sin_perfil_para_autos_reales()
    {
        var store = new ProfileStore();
        Assert.Null(store.Match(simulated: false, "8AXSMLTSCAN000017"));
        Assert.NotNull(store.Match(simulated: true, null));
    }

    private static Elm327Backend GetBackend(DiagnosticSession s)
    {
        var field = typeof(DiagnosticSession).GetField("_backend", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Elm327Backend)field.GetValue(s)!;
    }
}
