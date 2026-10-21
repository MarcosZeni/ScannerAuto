namespace TallerScan.Infrastructure.Elm;

public sealed record TraceEntry(
    long Id,
    DateTimeOffset AtUtc,
    string Command,
    string Response,
    string Status,
    long ElapsedMs);

// Registro TX/RX de solo lectura, con tope: nunca crece sin límite.
public sealed class TraceLog(int capacity = 2000)
{
    private readonly Lock _lock = new();
    private readonly LinkedList<TraceEntry> _entries = new();
    private long _nextId;

    public event Action<TraceEntry>? Added;

    public TraceEntry Add(string command, string response, string status, long elapsedMs)
    {
        TraceEntry entry;
        lock (_lock)
        {
            entry = new TraceEntry(++_nextId, DateTimeOffset.UtcNow, command, response, status, elapsedMs);
            _entries.AddLast(entry);
            while (_entries.Count > capacity)
                _entries.RemoveFirst();
        }
        Added?.Invoke(entry);
        return entry;
    }

    public IReadOnlyList<TraceEntry> Snapshot()
    {
        lock (_lock)
            return [.. _entries];
    }

    public void Clear()
    {
        lock (_lock)
            _entries.Clear();
    }
}
