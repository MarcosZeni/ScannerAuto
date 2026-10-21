using TallerScan.Core.Models;

namespace TallerScan.Core.Contracts;

// Canal de bytes hacia el adaptador (TCP, Bluetooth, simulador). No interpreta nada:
// una lectura puede traer media respuesta o varias juntas.
public interface IObdTransport : IAsyncDisposable
{
    string Description { get; }

    Task OpenAsync(CancellationToken cancellationToken);

    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    // Devuelve 0 cuando el otro extremo cerró la conexión.
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);
}

// Crea el transporte de cada tipo. Lo implementa la app según la plataforma.
public interface ITransportFactory
{
    bool IsAvailable(TransportKind kind);

    IObdTransport Create(AdapterEndpoint endpoint);
}

// Lista adaptadores ya vinculados al equipo. Lo implementa cada plataforma.
public interface IAdapterDiscovery
{
    // Si falta permiso o Bluetooth está apagado, Problem lo explica (no es una lista vacía muda).
    Task<DiscoveryResult> ListPairedAsync(CancellationToken cancellationToken);
}

public sealed record DiscoveryResult(IReadOnlyList<DiscoveredAdapter> Adapters, string? Problem);

// Backend de solo lectura: no existe un método para mandar comandos libres.
public interface IReadOnlyObdBackend : IAsyncDisposable
{
    Task<ConnectionReport> ConnectAsync(AdapterSettings settings, CancellationToken cancellationToken);

    Task<IReadOnlyList<EcuCapabilities>> DiscoverAsync(CancellationToken cancellationToken);

    // Una consulta funcional puede producir respuestas de varias ECU.
    Task<IReadOnlyList<PidReading>> ReadPidAsync(byte pid, CancellationToken cancellationToken);

    Task<IReadOnlyList<DtcModuleResult>> ReadDtcsAsync(DtcKind kind, CancellationToken cancellationToken);

    Task<IReadOnlyList<VinResult>> ReadVinAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MonitorStatus>> ReadMonitorStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<FreezeFrameResult>> ReadFreezeFrameAsync(IReadOnlyCollection<byte> pids, CancellationToken cancellationToken);

    // Tensión vista por el adaptador (ATRV): no es la misma medición que el PID 0x42.
    Task<double?> ReadAdapterVoltageAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}
