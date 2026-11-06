using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

/// <summary>
/// Keeps the API's warnings and errors so a test that waits on background work can say why it never finished: the
/// background work only logs its failures, and a flaky timeout is otherwise a silent one.
/// </summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public ILogger CreateLogger(string categoryName) => new Capture(categoryName, _entries);

    public void Dispose()
    {
    }

    /// <summary>What was logged at warning or above, or a note that nothing was.</summary>
    public string Describe() => _entries.IsEmpty
        ? "Nothing was logged at warning or above."
        : $"Logged at warning or above:{Environment.NewLine}{string.Join(Environment.NewLine, _entries)}";

    private sealed class Capture(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                entries.Enqueue($"[{logLevel}] {category}: {formatter(state, exception)}{(exception is null ? string.Empty : $" ({exception})")}");
            }
        }
    }
}
