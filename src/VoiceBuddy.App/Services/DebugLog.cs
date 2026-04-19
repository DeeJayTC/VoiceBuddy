namespace VoiceBuddy.Services;

public enum LogDirection { Out, In }

public sealed record LogEntry(DateTime Timestamp, LogDirection Direction, string Kind, string Payload);

/// <summary>
/// In-memory bounded log of API traffic (REST + WebSocket). Zero cost when Enabled is false.
/// Raw audio chunks are gated behind <see cref="LogAudioChunks"/> because they arrive ~8×/s
/// and drown out everything else.
/// </summary>
public sealed class DebugLog
{
    private const int MaxEntries = 1000;

    private readonly List<LogEntry> _entries = new(MaxEntries + 64);
    private readonly object _lock = new();

    public bool Enabled { get; set; }
    public bool LogAudioChunks { get; set; }

    public event EventHandler<LogEntry>? EntryAdded;
    public event EventHandler? Cleared;

    public void Log(LogDirection dir, string kind, string payload)
    {
        if (!Enabled) return;
        var entry = new LogEntry(DateTime.Now, dir, kind, payload);
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }
        EntryAdded?.Invoke(this, entry);
    }

    public void LogAudioChunk(int byteCount)
    {
        if (!Enabled || !LogAudioChunks) return;
        Log(LogDirection.Out, "source_media_chunk", $"{byteCount} bytes");
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock) return _entries.ToArray();
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
        Cleared?.Invoke(this, EventArgs.Empty);
    }
}
