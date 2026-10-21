using TallerScan.Core.Contracts;
using TallerScan.Core.Models;
using TallerScan.Infrastructure.Simulation;

namespace TallerScan.Infrastructure.Transports;

// Simulación y Wi-Fi funcionan en cualquier plataforma; Bluetooth clásico lo aporta
// la app (Android: RFCOMM; Windows: RfcommDeviceService). Si no hay implementación,
// el tipo figura como no disponible, nunca como una opción que falla en silencio.
public sealed class TransportFactory(Func<ClassicEndpoint, IObdTransport>? classic = null) : ITransportFactory
{
    // Último auto simulado creado, para que la app pueda apagar/prender su motor.
    public Elm327Emulator? CurrentEmulator { get; private set; }

    public bool IsAvailable(TransportKind kind) => kind switch
    {
        TransportKind.Simulation or TransportKind.Wifi => true,
        TransportKind.BluetoothClassic => classic is not null,
        _ => false,
    };

    public IObdTransport Create(AdapterEndpoint endpoint) => endpoint switch
    {
        SimulatedEndpoint s => new LoopbackTransport(CurrentEmulator = new Elm327Emulator(Scenarios.Create(s.ScenarioId), seed: Environment.TickCount)),
        WifiEndpoint w => new TcpTransport(w.Host, w.Port),
        ClassicEndpoint c when classic is not null => classic(c),
        _ => throw new NotSupportedException($"Transporte no disponible en este equipo: {endpoint.Kind}"),
    };
}
