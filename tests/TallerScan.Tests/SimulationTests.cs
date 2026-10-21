using TallerScan.Core.Models;
using TallerScan.Infrastructure.Elm;
using TallerScan.Infrastructure.History;
using TallerScan.Infrastructure.Reporting;
using TallerScan.Infrastructure.Sessions;
using TallerScan.Infrastructure.Simulation;
using TallerScan.Infrastructure.Transports;

namespace TallerScan.Tests;

// Punta a punta: sesión → backend ELM327 → transporte con fragmentos → emulador.
public class SimulationTests
{
    private static async Task<DiagnosticSession> ConnectAsync(string scenario)
    {
        var session = new DiagnosticSession(new TransportFactory(), new TraceLog());
        await session.ConnectAsync(new SimulatedEndpoint(scenario), CancellationToken.None);
        return session;
    }

    private static List<string> Codes(IEnumerable<DtcModuleResult> results) =>
        results.SelectMany(r => r.Codes).ToList();

    [Fact]
    public async Task Auto_sano_conecta_sin_fallas()
    {
        await using var s = await ConnectAsync("sano");
        Assert.Equal(SessionState.Ready, s.State);
        Assert.Equal("ELM327 v1.5", s.Report!.AdapterIdentifier);
        Assert.Contains("CAN 11", s.Report.Protocol);
        Assert.Contains((byte)0x0C, s.SupportedPids);
        Assert.Contains((byte)0x5E, s.SupportedPids); // tercer bloque de soporte
        Assert.Equal("8AXSMLTSCAN000017", s.Vin.Single().Vin);

        var scan = await s.ScanDtcsAsync(CancellationToken.None);
        Assert.All(scan.Stored, r => Assert.Equal(ReadStatus.Success, r.Status));
        Assert.Empty(Codes(scan.Stored));
        Assert.False(scan.Monitors.Single().MilOn);
    }

    [Fact]
    public async Task Falla_de_encendido_lee_las_tres_categorias_y_freeze_frame()
    {
        await using var s = await ConnectAsync("falla-encendido");
        var scan = await s.ScanDtcsAsync(CancellationToken.None);
        Assert.Equal(["P0301", "P0420"], Codes(scan.Stored));
        Assert.Equal(["P0171"], Codes(scan.Pending));
        Assert.Equal(["P0420"], Codes(scan.Permanent));
        Assert.True(scan.Monitors.Single().MilOn);
        Assert.Equal("7E8", scan.Stored.Single().Origin.Address);
        Assert.Equal("ECM-EngineControl", scan.Stored.Single().Origin.Name);

        var ff = scan.FreezeFrames.Single();
        Assert.Equal("P0301", ff.CausingDtc);
        var rpm = ff.Readings.Single(r => r.Pid == 0x0C);
        Assert.Equal(850, rpm.Value);
        var coolant = ff.Readings.Single(r => r.Pid == 0x05);
        Assert.Equal(83, coolant.Value);
    }

    [Fact]
    public async Task Dos_modulos_responden_por_separado()
    {
        await using var s = await ConnectAsync("multi-ecu");
        var scan = await s.ScanDtcsAsync(CancellationToken.None);
        Assert.Equal(2, scan.Stored.Count);
        Assert.Equal(["P0700"], scan.Stored.Single(r => r.Origin.Address == "7E8").Codes);
        Assert.Equal(["P0715"], scan.Stored.Single(r => r.Origin.Address == "7E9").Codes);
        Assert.Equal(["P0730"], scan.Pending.Single(r => r.Origin.Address == "7E9").Codes);
        Assert.Empty(scan.Pending.Single(r => r.Origin.Address == "7E8").Codes);
    }

    [Fact]
    public async Task Can_29_bits_conserva_origen()
    {
        await using var s = await ConnectAsync("can29");
        Assert.Contains("29 bits", s.Report!.Protocol);
        var scan = await s.ScanDtcsAsync(CancellationToken.None);
        Assert.Equal("18DAF110", scan.Stored.Single().Origin.Address);
        Assert.Equal(["P0128"], Codes(scan.Stored));
        Assert.Equal("8AXSMLTSCAN000058", s.Vin.Single().Vin);
    }

    [Fact]
    public async Task K_line_varias_lineas_y_sin_permanentes()
    {
        await using var s = await ConnectAsync("kwp-viejo");
        Assert.Contains("9141", s.Report!.Protocol);
        var scan = await s.ScanDtcsAsync(CancellationToken.None);
        Assert.Equal(["P0130", "P0135", "P0300", "P0505"], Codes(scan.Stored));
        // No soportado ≠ sin fallas.
        Assert.Equal(ReadStatus.NoData, scan.Permanent.Single().Status);
        Assert.Equal("8AXSMLTSCAN000066", s.Vin.Single().Vin);
    }

    [Fact]
    public async Task Contacto_apagado_adaptador_si_auto_no()
    {
        await using var s = await ConnectAsync("contacto-apagado");
        Assert.True(s.Report!.AdapterConnected);
        Assert.False(s.Report.VehicleResponding);
        Assert.Equal(SessionState.DetectingVehicle, s.State);
        Assert.Contains("UNABLE TO CONNECT", s.Report.Message);
    }

    [Fact]
    public async Task Lectura_en_vivo_entrega_valores()
    {
        await using var s = await ConnectAsync("sano");
        var readings = new List<PidReading>();
        var got = new TaskCompletionSource();
        s.LiveReading += r =>
        {
            lock (readings)
            {
                readings.Add(r);
                if (readings.Count(x => x.Pid == 0x0C) >= 3)
                    got.TrySetResult();
            }
        };
        s.StartRecording();
        s.StartLive([0x0C, 0x05, 0x0D]);
        await got.Task.WaitAsync(TimeSpan.FromSeconds(10));
        s.StopLive();
        var samples = s.StopRecording();
        lock (readings)
        {
            var rpm = readings.First(r => r.Pid == 0x0C);
            Assert.Equal(ReadStatus.Success, rpm.Status);
            Assert.InRange(rpm.Value!.Value, 700, 1300);
        }
        Assert.NotEmpty(samples);
        Assert.Contains("RPM del motor", CsvExport.Samples(samples));
    }

    [Fact]
    public async Task Comando_bloqueado_no_llega_al_adaptador()
    {
        var emulator = new Elm327Emulator(Scenarios.Create("falla-encendido"));
        var trace = new TraceLog();
        await using var link = new ElmLink(new LoopbackTransport(emulator), trace);
        await link.OpenAsync(CancellationToken.None);
        var r = await link.SendAsync("04", TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.Equal(ReadStatus.Blocked, r.Status);
        Assert.Equal(["P0301", "P0420"], emulator.Scenario.Ecus[0].Stored);
        Assert.Equal("BLOQUEADO", trace.Snapshot().Single().Status);
    }

    [Fact]
    public async Task Respuesta_tardia_no_contamina_la_siguiente()
    {
        var emulator = new Elm327Emulator(Scenarios.Create("lento"));
        await using var link = new ElmLink(new LoopbackTransport(emulator), new TraceLog());
        await link.OpenAsync(CancellationToken.None);
        await link.SendAsync("ATE0", TimeSpan.FromSeconds(3), CancellationToken.None);

        var late = await link.SendAsync("ATRV", TimeSpan.FromMilliseconds(100), CancellationToken.None);
        Assert.Equal(ReadStatus.Timeout, late.Status);

        var next = await link.SendAsync("ATDPN", TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.Equal(ReadStatus.Success, next.Status);
        Assert.Equal("A6", next.Text);
    }

    [Fact]
    public async Task Historial_guarda_lista_e_importa_sin_duplicar()
    {
        await using var s = await ConnectAsync("falla-encendido");
        await s.ScanDtcsAsync(CancellationToken.None);
        var record = SessionRecordBuilder.From(s, [], "ab 123 cd", "Gol Trend 1.6", "Tironea en frío <b>");
        Assert.True(record.Simulated);
        Assert.Equal("AB 123 CD", record.Plate);
        Assert.True(record.MilOn);

        var dir = Path.Combine(Path.GetTempPath(), "tallerscan-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonHistoryStore(dir);
            await store.SaveAsync(record);
            var list = await store.ListAsync();
            Assert.Equal(3, list.Single().DtcCount);

            var html = HtmlReport.Build((await store.LoadAsync(record.Id))!);
            Assert.Contains("P0301", html);
            Assert.Contains("DATOS SIMULADOS", html);
            Assert.Contains("&lt;b&gt;", html);
            Assert.DoesNotContain("<b>\"", html);

            await using var file = File.OpenRead(store.FileOf(record.Id));
            var again = await store.ImportAsync(file);
            Assert.False(again.Ok);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Csv_neutraliza_formulas() =>
        Assert.Equal("\"'=SUMA(A1)\"", CsvExport.Cell("=SUMA(A1)"));
}
