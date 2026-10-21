using TallerScan.Core.Models;

namespace TallerScan.App.Services;

// Datos del diagnóstico en curso que comparten las pantallas: vehículo, notas y
// últimas lecturas en vivo (para guardarlas en el historial).
public sealed class SessionContext
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string, byte), PidReading> _last = [];

    public string Plate { get; set; } = "";

    public string Vehicle { get; set; } = "";

    public string Notes { get; set; } = "";

    public void Remember(PidReading reading)
    {
        if (reading.Status != ReadStatus.Success)
            return;
        lock (_lock)
            _last[(reading.Origin.Address ?? "", reading.Pid)] = reading;
    }

    public IReadOnlyList<PidReading> LastReadings()
    {
        lock (_lock)
            return [.. _last.Values.OrderBy(r => r.Pid)];
    }

    public void ClearReadings()
    {
        lock (_lock)
            _last.Clear();
    }
}
