using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using TallerScan.Core.Contracts;
using TallerScan.Core.Models;
using TallerScan.Infrastructure.Transports;

namespace TallerScan.App.Platform;

// Adaptadores Bluetooth clásicos vinculados en Windows que ofrecen puerto serie (SPP).
public sealed class PlatformBluetooth : IAdapterDiscovery
{
    public async Task<DiscoveryResult> ListPairedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var selector = RfcommDeviceService.GetDeviceSelector(RfcommServiceId.SerialPort);
            var devices = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
            var list = devices
                .Select(d => new DiscoveredAdapter(d.Name, new ClassicEndpoint(d.Id, d.Name), "Bluetooth"))
                .OrderByDescending(a => a.DisplayName.Contains("OBD", StringComparison.OrdinalIgnoreCase) || a.DisplayName.Contains("ELM", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return new DiscoveryResult(list, list.Count == 0
                ? "No hay adaptadores Bluetooth vinculados. Vinculalo en Configuración → Bluetooth y dispositivos (PIN habitual 1234 o 0000)."
                : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DiscoveryResult([], "No se pudo consultar el Bluetooth de la PC: " + ex.Message);
        }
    }
}

public sealed class BluetoothClassicTransport(ClassicEndpoint endpoint) : StreamTransport
{
    private StreamSocket? _socket;
    private RfcommDeviceService? _service;

    public override string Description => $"Bluetooth {endpoint.DisplayName}";

    protected override async Task<Stream> OpenStreamAsync(CancellationToken cancellationToken)
    {
        _service = await RfcommDeviceService.FromIdAsync(endpoint.DeviceId).AsTask(cancellationToken)
                   ?? throw new IOException("No se pudo acceder al adaptador (¿apagado, fuera de alcance o sin permiso de Bluetooth?).");
        _socket = new StreamSocket();
        await _socket.ConnectAsync(_service.ConnectionHostName, _service.ConnectionServiceName,
            SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(cancellationToken);
        // Sin buffer de lectura: cada fragmento tiene que llegar enseguida al armador de respuestas.
        return new DuplexStream(_socket.InputStream.AsStreamForRead(0), _socket.OutputStream.AsStreamForWrite(0));
    }

    protected override ValueTask CloseExtraAsync()
    {
        _socket?.Dispose();
        _socket = null;
        _service?.Dispose();
        _service = null;
        return ValueTask.CompletedTask;
    }
}
