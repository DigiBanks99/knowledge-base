using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.IntegrationTests;

/// <summary>
/// Keeps the warnings and errors the app logs. The pipeline handles most failures by logging them and moving on, so
/// these explain a test that timed out waiting for the pipeline.
/// </summary>
internal sealed class ProblemLog : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                string reason = exception is null ? string.Empty : $" ({exception.GetType().Name}: {exception.Message})";
                entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)}{reason}");
            }
        }
    }
}