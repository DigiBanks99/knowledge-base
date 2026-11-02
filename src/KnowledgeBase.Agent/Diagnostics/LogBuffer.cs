using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.Diagnostics;

/// <summary>
/// Keeps the most recent log entries in memory so the Logs page can show what the API did, without a log server. It is
/// deliberately not written to the wiki: the wiki root is committed and pushed to its backup remote, and logs are not
/// wiki content. A restart empties it; the runs that record searches and agent work survive restarts.
/// </summary>
internal sealed class LogBuffer(int capacity)
{
    public const int DefaultCapacity = 5000;

    private readonly Lock _gate = new();
    private readonly Queue<LogEntry> _entries = new(capacity);
    private long _lastSequence;

    public void Add(DateTimeOffset at, LogLevel level, string category, string message, string? exception)
    {
        lock (_gate)
        {
            _entries.Enqueue(new LogEntry(++_lastSequence, at, level, category, message, exception));
            while (_entries.Count > capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    /// <summary>
    /// The newest entries first, at or above <paramref name="minimumLevel"/>, and where <paramref name="search"/> (if
    /// given) appears in the message, category or exception, ignoring case.
    /// </summary>
    public IReadOnlyList<LogEntry> Query(LogLevel minimumLevel, string? search, int limit)
    {
        lock (_gate)
        {
            return [.. _entries
                .Reverse()
                .Where(entry => entry.Level >= minimumLevel)
                .Where(entry => search is null || Matches(entry, search))
                .Take(limit)];
        }
    }

    private static bool Matches(LogEntry entry, string search) =>
        entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
        || entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (entry.Exception?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
}
