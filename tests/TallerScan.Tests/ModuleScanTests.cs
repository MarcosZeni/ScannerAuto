using TallerScan.Core.Actuators;
using TallerScan.Core.Diagnostics;
using TallerScan.Core.Models;
using TallerScan.Core.Safety;
using TallerScan.Infrastructure.Elm;
using TallerScan.Infrastructure.History;
using TallerScan.Infrastructure.Modules;
using TallerScan.Infrastructure.Reporting;
using TallerScan.Infrastructure.Sessions;
using TallerScan.Infrastructure.Simulation;
using TallerScan.Infrastructure.Transports;

namespace TallerScan.Tests;

// Barrido de módulos sin perfil: encontrar ABS, airbag, caja y carrocería por CAN,
// leer sus fallas (UDS 19 02 / KWP 18) y borrarlas (14), con los límites reales.
public class ModuleScanTests
{
    private static readonly ObdProtocol Can11 = new('6', "CAN 11");
    private static readonly ObdProtocol Can29 = new('7', "CAN 29");

    private static async Task<DiagnosticSession> ConnectAsync(string scenario)
    {
        var session = new DiagnosticSession(new TransportFactory(), new TraceLog());
        await session.ConnectAsync(new SimulatedEndpoint(scenario), CancellationToken.None);
        Assert.Equal(SessionState.Ready, session.State);
        session.Simulator!.Engine.Mode = EngineMode.Off;
        return session;
    }

    // Direcciones de prueba: las OBD más las de los módulos del escenario, para no barrer 480 en cada test.
    private static List<ModuleAddress> ShortList() =>
        new[] { 0x7E0, 0x7E1, 0x7E2, 0x7B0, 0x752, 0x7C0, 0x7A0, 0x6A8 }
            .Select(id => new ModuleAddress($"Módulo {id:X3}", $"{id:X3}", ModuleAddressBook.ExpectedResponse11(id))).ToList();

    private static Elm327Backend Backend(DiagnosticSession s)
    {
        var field = typeof(DiagnosticSession).GetField("_backend", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Elm327Backend)field.GetValue(s)!;
    }

    private static async Task<ModuleScanResult> SweepAsync(DiagnosticSession s, IReadOnlyList<ModuleAddress>? candidates = null, IProgress<ModuleScanProgress>? progress = null, CancellationToken ct = default)
    {
        var backend = Backend(s);
        var scanner = backend.CreateModuleScanner();
        using (backend.OpenScope(ModuleScanner.ScanScope(backend.Protocol)))
            return await scanner.ScanAsync(candidates ?? ShortList(), progress, ct);
    }

    [Fact]
    public void Alcances_del_barrido_y_del_borrado()
    {
        var scan = ModuleScanner.ScanScope(Can11);
        Assert.True(CommandPolicy.IsAllowed("ATSH7B0", scan, out _));
        Assert.True(CommandPolicy.IsAllowed("ATSH6A8", scan, out _));
        Assert.True(CommandPolicy.IsAllowed("ATCRA7B8", scan, out _));
        Assert.True(CommandPolicy.IsAllowed("ATCF600", scan, out _));
        Assert.True(CommandPolicy.IsAllowed("ATCM600", scan, out _));
        Assert.True(CommandPolicy.IsAllowed("ATST19", scan, out _));
        Assert.True(CommandPolicy.IsAllowed("19022F", scan, out _));
        Assert.True(CommandPolicy.IsAllowed("1800FF00", scan, out _));
        // Es solo lectura: nada de borrar, sesión ni actuadores.
        Assert.False(CommandPolicy.IsAllowed("14FFFFFF", scan, out _));
        Assert.False(CommandPolicy.IsAllowed("1003", scan, out _));
        Assert.False(CommandPolicy.IsAllowed("2F40010301", scan, out _));
        Assert.False(CommandPolicy.IsAllowed("2701", scan, out _));
        Assert.False(CommandPolicy.IsAllowed("ATSH7E0", CommandScope.ReadOnly, out _));
        Assert.False(CommandPolicy.IsAllowed("19022F", CommandScope.ReadOnly, out _));

        var clear = ModuleScanner.ClearScope(new ModuleAddress("ABS", "7B0", "7B8"));
        Assert.True(CommandPolicy.IsAllowed("ATSH7B0", clear, out _));
        Assert.True(CommandPolicy.IsAllowed("14FFFFFF", clear, out _));
        Assert.True(CommandPolicy.IsAllowed("14FF00", clear, out _));
        Assert.True(CommandPolicy.IsAllowed("1003", clear, out _));
        Assert.True(CommandPolicy.IsAllowed("1001", clear, out _));
        Assert.False(CommandPolicy.IsAllowed("ATSH7E0", clear, out _));   // otro módulo
        Assert.False(CommandPolicy.IsAllowed("2F40010301", clear, out _));
        Assert.False(CommandPolicy.IsAllowed("2701", clear, out _));

        var scan29 = ModuleScanner.ScanScope(Can29);
        Assert.True(CommandPolicy.IsAllowed("ATSHDA28F1", scan29, out _));
        Assert.True(CommandPolicy.IsAllowed("ATCRA18DAF128", scan29, out _));
        Assert.True(CommandPolicy.IsAllowed("ATCF18DAF100", scan29, out _));
    }

    [Theory]
    [InlineData(0x01, 0x71, 0x00, 0x29, "P0171", "presente ahora, confirmada")]
    [InlineData(0x40, 0x35, 0x00, 0x24, "C0035", "pendiente")]
    [InlineData(0xC1, 0x55, 0x1C, 0xA9, "U0155-1C", "presente ahora, confirmada, luz encendida")]
    [InlineData(0x80, 0x01, 0x00, 0x20, "B0001", "histórica (ocurrió desde el último borrado)")]
    public void Codigos_uds_de_tres_bytes_y_estado(byte a, byte b, byte ftb, byte status, string display, string text)
    {
        var dtc = DtcCodec.DecodeUds(a, b, ftb, status)!;
        Assert.Equal(display, dtc.Display);
        Assert.Equal(text, dtc.StatusText);
        Assert.Null(DtcCodec.DecodeUds(0, 0, 0, 0x29));
    }

    [Fact]
    public void Estado_kwp_se_traduce()
    {
        var kwp = DtcCodec.DecodeKwp(0xC1, 0x55, 0xE0)!;
        Assert.Equal("U0155", kwp.Display);
        Assert.True(kwp.IsActive);
        Assert.True(kwp.IsConfirmed);
        Assert.True(kwp.WarningLamp);
    }

    [Fact]
    public async Task Barrido_encuentra_todos_los_modulos()
    {
        await using var s = await ConnectAsync("todos-los-modulos");
        var reports = new List<ModuleScanProgress>();
        var result = await SweepAsync(s, progress: new SyncProgress(reports.Add));

        Assert.True(result.Completed);
        Assert.Equal(ShortList().Count, result.Probed);
        Assert.Equal(["772", "7B8", "7C8", "7E8", "7E9"], result.Modules.Select(m => m.Address.ResponseId).Order().ToArray());
        Assert.Contains(reports, p => p.Found > 0);

        var engine = result.Modules.Single(m => m.Address.ResponseId == "7E8");
        Assert.Equal("ECM-EngineControl", engine.Name);
        Assert.Equal("7E0", engine.Address.RequestId);
        Assert.Contains("Motor", engine.Address.Name);
        Assert.Equal(["P0420"], engine.Dtcs.Select(d => d.Display).ToArray());
        Assert.True(engine.Dtcs[0].WarningLamp);

        var abs = result.Modules.Single(m => m.Address.ResponseId == "7B8");
        Assert.Equal(ModuleDialect.Uds, abs.Dialect);
        Assert.Equal(["C0035", "C0050"], abs.Dtcs.Select(d => d.Display).Order().ToArray());
        Assert.True(abs.Dtcs.Single(d => d.Code == "C0050").IsPending);
        Assert.Contains("chasis", abs.SystemHint);
        Assert.Equal("Módulo 7B8", abs.DisplayName);

        // El airbag no sigue "pedido + 8": se encontró gracias al filtro amplio.
        var airbag = result.Modules.Single(m => m.Address.ResponseId == "772");
        Assert.Equal("752", airbag.Address.RequestId);
        Assert.Contains("carrocería", airbag.SystemHint);

        var body = result.Modules.Single(m => m.Address.ResponseId == "7C8");
        Assert.Equal(ModuleDialect.Kwp, body.Dialect);
        Assert.Equal(["U0155"], body.Dtcs.Select(d => d.Display).ToArray());

        // Al terminar, el adaptador volvió al pedido funcional y al filtro automático: OBD2 sigue andando.
        var trace = s.Trace.Snapshot();
        var last = trace.Select(t => t.Command).TakeLast(3).ToArray();
        Assert.Equal(["ATSH7DF", "ATAR", "ATST32"], last);
        var scan = await s.ScanDtcsAsync(CancellationToken.None);
        Assert.Equal(["P0420", "P0715"], scan.Stored.SelectMany(r => r.Codes).Order().ToArray());
    }

    [Fact]
    public async Task Sin_filtro_amplio_se_usa_la_convencion_mas_8()
    {
        await using var s = await ConnectAsync("todos-los-modulos");
        s.Simulator!.SupportsWideFilter = false;
        var result = await SweepAsync(s);
        Assert.True(result.Completed);
        // El airbag (752 → 772) no se puede encontrar así; el resto sí.
        Assert.Equal(["7B8", "7C8", "7E8", "7E9"], result.Modules.Select(m => m.Address.ResponseId).Order().ToArray());
        Assert.Contains(s.Trace.Snapshot(), t => t.Command == "ATCRA7B8");
    }

    [Fact]
    public async Task Barrido_de_29_bits()
    {
        await using var s = await ConnectAsync("can29");
        var backend = Backend(s);
        var scanner = backend.CreateModuleScanner();
        var candidates = new[] { (byte)0x10, (byte)0x18, (byte)0x28 }
            .Select(t => new ModuleAddress($"Módulo {t:X2}", ModuleAddressBook.Request29(t), ModuleAddressBook.Response29(t))).ToList();
        ModuleScanResult result;
        using (backend.OpenScope(ModuleScanner.ScanScope(backend.Protocol)))
            result = await scanner.ScanAsync(candidates, null, CancellationToken.None);
        var engine = Assert.Single(result.Modules);
        Assert.Equal("18DAF110", engine.Address.ResponseId);
        Assert.Equal(["P0128"], engine.Dtcs.Select(d => d.Display).ToArray());
        Assert.Contains(s.Trace.Snapshot(), t => t.Command == "ATSHDA10F1");
    }

    [Fact]
    public async Task Kline_no_tiene_barrido()
    {
        await using var s = await ConnectAsync("kwp-viejo");
        Assert.False(s.CanScanModules);
        var result = await s.ScanModulesAsync(null, CancellationToken.None);
        Assert.False(result.Completed);
        Assert.Empty(result.Modules);
        Assert.Contains("CAN", result.Message);
    }

    [Fact]
    public async Task Cancelar_deja_el_adaptador_en_orden()
    {
        await using var s = await ConnectAsync("todos-los-modulos");
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.Probed >= 2) cts.Cancel(); });
        var result = await SweepAsync(s, progress: progress, ct: cts.Token);
        Assert.False(result.Completed);
        Assert.True(result.Probed < ShortList().Count);
        Assert.Contains("cancelado", result.Message);
        Assert.Contains(s.Trace.Snapshot().TakeLast(3), t => t.Command == "ATAR");
        var scan = await s.ScanDtcsAsync(CancellationToken.None);
        Assert.Equal(2, scan.Stored.Count);
    }

    [Fact]
    public async Task Borrar_por_modulo_sin_perfil()
    {
        await using var s = await ConnectAsync("todos-los-modulos");
        var history = new JsonHistoryStore(Path.Combine(Path.GetTempPath(), "tallerscan-tests", Guid.NewGuid().ToString("N")));
        var sweep = await SweepAsync(s);
        // La sesión no sabe de este barrido corto: se lo cargamos como si fuera el completo.
        typeof(DiagnosticSession).GetProperty(nameof(DiagnosticSession.LastModuleScan))!.SetValue(s, sweep);

        // ABS: rechaza en sesión por defecto, la app abre la extendida, borra y la cierra.
        var abs = sweep.Modules.Single(m => m.Address.ResponseId == "7B8");
        var r = await s.ClearModuleDtcsAsync(abs, CancellationToken.None);
        Assert.True(r.Accepted, r.Message);
        Assert.Empty(r.After.Dtcs);
        Assert.Contains("Sin fallas al releer", r.Message);
        var commands = s.Trace.Snapshot().Select(t => t.Command).ToList();
        int first = commands.IndexOf("14FFFFFF");
        Assert.True(first >= 0);
        Assert.Equal("1003", commands[first + 1]);
        Assert.Equal("14FFFFFF", commands[first + 2]);
        Assert.Equal("1001", commands[first + 3]);
        Assert.Empty(s.LastModuleScan!.Modules.Single(m => m.Address.ResponseId == "7B8").Dtcs);

        // Airbag con datos de choque: ningún escáner lo borra; la app lo explica y no insiste con otra cosa.
        var airbag = sweep.Modules.Single(m => m.Address.ResponseId == "772");
        r = await s.ClearModuleDtcsAsync(airbag, CancellationToken.None);
        Assert.False(r.Accepted);
        Assert.Contains("Condiciones no cumplidas", r.Message);
        Assert.Single(r.After.Dtcs);
        Assert.DoesNotContain(s.Trace.Snapshot(), t => t.Command.StartsWith("27"));

        // Carrocería KWP: se borra con 14 FF 00.
        var body = sweep.Modules.Single(m => m.Address.ResponseId == "7C8");
        r = await s.ClearModuleDtcsAsync(body, CancellationToken.None);
        Assert.True(r.Accepted, r.Message);
        Assert.Empty(r.After.Dtcs);
        Assert.Contains(s.Trace.Snapshot(), t => t.Command == "14FF00");

        // La caja ignoraba el 04 genérico: por módulo sí se borra.
        var tcm = sweep.Modules.Single(m => m.Address.ResponseId == "7E9");
        r = await s.ClearModuleDtcsAsync(tcm, CancellationToken.None);
        Assert.True(r.Accepted, r.Message);
        var scan = await s.ScanDtcsAsync(CancellationToken.None);
        Assert.Equal(["P0420"], scan.Stored.SelectMany(x => x.Codes).ToArray());

        // Todo queda en el historial y en el informe.
        Assert.Equal(4, s.Actions.Count);
        Assert.Equal(3, s.Actions.Count(a => a.Ok));
        var record = SessionRecordBuilder.From(s, [], "AB 123 CD", "", "");
        await history.SaveAsync(record);
        Assert.NotNull(record.ModuleScan);
        Assert.Equal(5, record.ModuleScan!.Modules.Count);
        var html = HtmlReport.Build(record);
        Assert.Contains("Otros módulos (barrido por CAN)", html);
        Assert.Contains("B0001", html);
        Assert.Contains("Borrado de fallas por m&#243;dulo", html); // acentos escapados en el HTML
        Assert.Contains("barrido de módulos por CAN", html);
        var summary = (await history.ListAsync()).Single();
        Assert.Equal(2, summary.DtcCount); // P0420 del motor + B0001 del airbag (lo demás se borró)
    }

    [Fact]
    public async Task Barrido_completo_desde_la_sesion()
    {
        await using var s = await ConnectAsync("multi-ecu");
        Assert.True(s.CanScanModules);
        var result = await s.ScanModulesAsync(null, CancellationToken.None);
        Assert.True(result.Completed);
        Assert.Equal(ModuleAddressBook.Requests11().Count, result.Total);
        Assert.Equal(["7E8", "7E9"], result.Modules.Select(m => m.Address.ResponseId).Order().ToArray());
        Assert.Same(result, s.LastModuleScan);
    }

    // IProgress sin SynchronizationContext: avisa en el mismo hilo, para que los tests sean deterministas.
    private sealed class SyncProgress(Action<ModuleScanProgress> handler) : IProgress<ModuleScanProgress>
    {
        public void Report(ModuleScanProgress value) => handler(value);
    }
}
