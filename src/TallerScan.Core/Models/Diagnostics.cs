namespace TallerScan.Core.Models;

// Estado de cada lectura. Una lectura fallida nunca se convierte en cero:
// el valor queda null y el estado dice por qué.
public enum ReadStatus
{
    Success,
    NoData,
    Unsupported,
    Timeout,
    InvalidResponse,
    CommunicationError,
    Cancelled,
    NotQueried,
    Blocked
}

public enum DtcKind { Stored, Pending, Permanent }

// Address puede ser null si no se pudo identificar el emisor.
// Nunca convertir ese null automáticamente en "motor" o "7E8".
public sealed record EcuOrigin(string? Address, string? Name)
{
    public static readonly EcuOrigin Unknown = new(null, null);

    public string DisplayName => (Name, Address) switch
    {
        (not null, not null) => $"{Name} ({Address})",
        (null, not null) => Address,
        (not null, null) => Name,
        _ => "Origen desconocido"
    };
}

public sealed record PidReading(
    EcuOrigin Origin,
    byte Pid,
    double? Value,
    string Unit,
    ReadStatus Status,
    DateTimeOffset ReceivedAtUtc,
    string? RawTraceId);

public sealed record EcuCapabilities(
    EcuOrigin Origin,
    IReadOnlySet<byte> SupportedMode01Pids,
    ReadStatus Status);

public sealed record DtcModuleResult(
    EcuOrigin Origin,
    DtcKind Kind,
    IReadOnlyList<string> Codes,
    ReadStatus Status,
    DateTimeOffset ReceivedAtUtc,
    string? RawTraceId,
    string? Message = null);

public sealed record VinResult(
    EcuOrigin Origin,
    string? Vin,
    ReadStatus Status,
    string? RawTraceId);

// PID 01 del modo 01: luz de avería, cantidad de fallas y monitores de emisiones.
public sealed record MonitorTest(string Name, bool Available, bool Complete);

public sealed record MonitorStatus(
    EcuOrigin Origin,
    ReadStatus Status,
    bool? MilOn,
    int? DtcCount,
    bool? IsDiesel,
    IReadOnlyList<MonitorTest> Monitors);

// Una lectura del freeze frame (modo 02, frame 0).
public sealed record FreezeFrameResult(
    EcuOrigin Origin,
    string? CausingDtc,
    IReadOnlyList<PidReading> Readings,
    ReadStatus Status);

public sealed record ConnectionReport(
    bool AdapterConnected,
    bool VehicleResponding,
    string? AdapterIdentifier,
    string? Protocol,
    double? AdapterVoltage,
    string? Message);

public enum SessionState
{
    Disconnected,
    OpeningTransport,
    InitializingAdapter,
    DetectingVehicle,
    DiscoveringCapabilities,
    Ready,
    Faulted
}
