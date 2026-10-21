using System.Net;
using System.Net.Sockets;
using System.Text;
using TallerScan.Core.Models;
using TallerScan.Infrastructure.Elm;
using TallerScan.Infrastructure.Sessions;
using TallerScan.Infrastructure.Simulation;
using TallerScan.Infrastructure.Transports;

namespace TallerScan.Tests;

// El mismo camino que un adaptador Wi-Fi: socket TCP real contra el emulador.
public class TcpTests
{
    [Fact]
    public async Task Sesion_por_wifi_contra_emulador()
    {
        var emulator = new Elm327Emulator(Scenarios.Create("multi-ecu"));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(cts.Token);
            var stream = client.GetStream();
            var buffer = new byte[64];
            var pending = new StringBuilder();
            while (!cts.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buffer, cts.Token);
                if (n == 0)
                    return;
                foreach (var b in buffer[..n])
                {
                    if (b == '\r')
                    {
                        var response = Encoding.ASCII.GetBytes(emulator.Process(pending.ToString()));
                        pending.Clear();
                        // Se manda partido en dos para probar el armado por '>'.
                        await stream.WriteAsync(response.AsMemory(0, response.Length / 2), cts.Token);
                        await stream.FlushAsync(cts.Token);
                        await stream.WriteAsync(response.AsMemory(response.Length / 2), cts.Token);
                    }
                    else
                    {
                        pending.Append((char)b);
                    }
                }
            }
        });

        try
        {
            await using var session = new DiagnosticSession(new TransportFactory(), new TraceLog());
            var report = await session.ConnectAsync(new WifiEndpoint("127.0.0.1", port), cts.Token);
            Assert.True(report.VehicleResponding, report.Message);
            Assert.Equal(SessionState.Ready, session.State);
            var scan = await session.ScanDtcsAsync(cts.Token);
            Assert.Equal(["P0700", "P0715"], scan.Stored.SelectMany(r => r.Codes).Order().ToArray());
            await session.DisconnectAsync();
        }
        finally
        {
            listener.Stop();
            await cts.CancelAsync();
            try { await server; } catch { }
        }
    }

    [Fact]
    public async Task Adaptador_mudo_queda_desconectado_y_no_como_conectado()
    {
        // Acepta la conexión TCP pero nunca contesta (otro cliente lo tiene tomado, o no es un ELM).
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            await using var session = new DiagnosticSession(new TransportFactory(), new TraceLog());
            var report = await session.ConnectAsync(new WifiEndpoint("127.0.0.1", port), CancellationToken.None);
            Assert.False(report.AdapterConnected);
            Assert.Equal(SessionState.Faulted, session.State);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Puerto_cerrado_informa_sin_colgarse()
    {
        await using var session = new DiagnosticSession(new TransportFactory(), new TraceLog());
        var report = await session.ConnectAsync(new WifiEndpoint("127.0.0.1", 1), CancellationToken.None);
        Assert.False(report.AdapterConnected);
        Assert.Equal(SessionState.Faulted, session.State);
    }
}
