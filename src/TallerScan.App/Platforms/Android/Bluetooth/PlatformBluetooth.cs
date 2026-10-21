using Android.Bluetooth;
using Android.Content;
using Java.Util;
using TallerScan.Core.Contracts;
using TallerScan.Core.Models;
using TallerScan.Infrastructure.Transports;

namespace TallerScan.App.Platform;

// Android 12+ pide BLUETOOTH_CONNECT en tiempo de ejecución para usar equipos vinculados.
// En versiones anteriores alcanza con lo declarado en el manifest.
public sealed class BluetoothConnectPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? [(Android.Manifest.Permission.BluetoothConnect, true)]
            : [];
}

// Lista los adaptadores ya vinculados en Ajustes. No escanea: no hace falta ubicación
// ni BLUETOOTH_SCAN, y el mecánico elige el suyo por nombre.
public sealed class PlatformBluetooth : IAdapterDiscovery
{
    public static BluetoothAdapter? Adapter =>
        (global::Android.App.Application.Context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;

    public static Task<bool> EnsurePermissionAsync() =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var status = await Permissions.CheckStatusAsync<BluetoothConnectPermission>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<BluetoothConnectPermission>();
            return status == PermissionStatus.Granted;
        });

    public async Task<DiscoveryResult> ListPairedAsync(CancellationToken cancellationToken)
    {
        if (!await EnsurePermissionAsync())
            return new DiscoveryResult([], "Sin permiso de Bluetooth. Habilitalo en Ajustes → Apps → TallerScan → Permisos → Dispositivos cercanos.");
        var adapter = Adapter;
        if (adapter is null)
            return new DiscoveryResult([], "Este equipo no tiene Bluetooth.");
        if (!adapter.IsEnabled)
            return new DiscoveryResult([], "El Bluetooth está apagado. Prendelo y volvé a buscar.");

        var list = (adapter.BondedDevices ?? [])
            .Where(d => d.Address is not null)
            .Select(d => new DiscoveredAdapter(d.Name ?? d.Address!, new ClassicEndpoint(d.Address!, d.Name ?? d.Address!), d.Address))
            .OrderByDescending(a => a.DisplayName.Contains("OBD", StringComparison.OrdinalIgnoreCase) || a.DisplayName.Contains("ELM", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return new DiscoveryResult(list, null);
    }
}

// Bluetooth clásico (SPP / RFCOMM), el de la mayoría de los ELM327 baratos.
public sealed class BluetoothClassicTransport(ClassicEndpoint endpoint) : StreamTransport
{
    private static readonly UUID SerialPortProfile = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;

    private BluetoothSocket? _socket;

    public override string Description => $"Bluetooth {endpoint.DisplayName}";

    protected override async Task<Stream> OpenStreamAsync(CancellationToken cancellationToken)
    {
        if (!await PlatformBluetooth.EnsurePermissionAsync())
            throw new IOException("Falta el permiso de Bluetooth (Dispositivos cercanos).");
        var adapter = PlatformBluetooth.Adapter ?? throw new IOException("Este equipo no tiene Bluetooth.");
        if (!adapter.IsEnabled)
            throw new IOException("El Bluetooth está apagado.");
        var device = adapter.GetRemoteDevice(endpoint.DeviceId) ?? throw new IOException("No se encontró el adaptador vinculado.");

        // Cancelar el token cierra el socket: es la única forma de cortar un connect colgado.
        var socket = device.CreateRfcommSocketToServiceRecord(SerialPortProfile)!;
        _socket = socket;
        using (cancellationToken.Register(() => TryClose(_socket)))
        {
            try
            {
                await socket.ConnectAsync();
            }
            catch (Java.IO.IOException) when (!cancellationToken.IsCancellationRequested)
            {
                // Algunos clones solo aceptan la conexión sin cifrar.
                TryClose(socket);
                socket = device.CreateInsecureRfcommSocketToServiceRecord(SerialPortProfile)!;
                _socket = socket;
                try
                {
                    await socket.ConnectAsync();
                }
                catch (Java.IO.IOException ex)
                {
                    TryClose(socket);
                    throw new IOException("No se pudo conectar con el adaptador. ¿Está enchufado en el auto y cerca? " + ex.Message);
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new DuplexStream(socket.InputStream!, socket.OutputStream!);
    }

    private static void TryClose(BluetoothSocket? socket)
    {
        try
        {
            socket?.Close();
        }
        catch (Java.IO.IOException)
        {
        }
    }

    protected override ValueTask CloseExtraAsync()
    {
        TryClose(_socket);
        _socket = null;
        return ValueTask.CompletedTask;
    }
}
