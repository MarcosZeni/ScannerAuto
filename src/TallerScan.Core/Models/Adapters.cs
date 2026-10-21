namespace TallerScan.Core.Models;

public enum TransportKind { Simulation, BluetoothClassic, Wifi, Ble, Serial }

// Configuración de transporte discriminada: cada tipo trae solo lo que necesita.
public abstract record AdapterEndpoint
{
    public abstract TransportKind Kind { get; }
}

public sealed record SimulatedEndpoint(string ScenarioId) : AdapterEndpoint
{
    public override TransportKind Kind => TransportKind.Simulation;
}

// DeviceId es un identificador opaco de la plataforma (MAC en Android, id de
// dispositivo en Windows), no una identidad compartida entre equipos.
public sealed record ClassicEndpoint(string DeviceId, string DisplayName) : AdapterEndpoint
{
    public override TransportKind Kind => TransportKind.BluetoothClassic;
}

public sealed record WifiEndpoint(string Host, int Port) : AdapterEndpoint
{
    public override TransportKind Kind => TransportKind.Wifi;
}

public sealed record AdapterSettings(AdapterEndpoint Endpoint);

public sealed record DiscoveredAdapter(string DisplayName, AdapterEndpoint Endpoint, string? Detail)
{
    public override string ToString() => Detail is null ? DisplayName : $"{DisplayName} ({Detail})";
}
