using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.Diagnostics;

/// <summary>
/// Keeps the most recent log entries in memory so the Logs page can read them quickly. Every entry is also written to a
/// file by <see cref="LogFiles"/>; this buffer is what is loaded back from those files after a restart.
/// </summary>
internal sealed class LogBuffer(int capacity)
{
    public const int DefaultCapacity = 5000;

    private readonly Lock _gate = new();
    private readonly Queue<LogEntry> _entries = new(capacity);
    private long _lastSequence;

    public LogEntry Add(
        DateTimeOffset at,
        LogLevel level,
        string area,
        string category,
        string message,
        IReadOnlyDictionary<string, string> properties,
        string? exception)
    {
        lock (_gate)
        {
            LogEntry entry = new(++_lastSequence, at, level, area, category, message, properties, exception);
            Enqueue(entry);
            return entry;
        }
    }

    /// <summary>Loads entries read back from disk, given oldest first, as if they had just been logged.</summary>
    public void Restore(IEnumerable<LogEntry> oldestFirst)
    {
        lock (_gate)
        {
            foreach (LogEntry entry in oldestFirst)
            {
                Enqueue(entry with { Sequence = ++_lastSequence });
            }
        }
    }

    /// <summary>
    /// The newest entries first, at or above <paramref name="minimumLevel"/>, from <paramref name="area"/> when given, and
    /// where <paramref name="search"/> (if given) appears in the message, category, exception or any property value,
    /// ignoring case.
    /// </summary>
    public IReadOnlyList<LogEntry> Query(LogLevel minimumLevel, string? area, string? search, int limit)
    {
        lock (_gate)
        {
            return [.. _entries
                .Reverse()
                .Where(entry => entry.Level >= minimumLevel)
                .Where(entry => area is null || string.Equals(entry.Area, area, StringComparison.OrdinalIgnoreCase))
                .Where(entry => search is null || Matches(entry, search))
                .Take(limit)];
        }
    }

    private void Enqueue(LogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > capacity)
        {
            _entries.Dequeue();
        }
    }

    private static bool Matches(LogEntry entry, string search) =>
        entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
        || entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (entry.Exception?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
        || entry.Properties.Values.Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase));
}
