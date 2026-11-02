using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.Diagnostics;

/// <summary>
/// Sends every logger to the <see cref="LogBuffer"/> as well as to the console and OpenTelemetry. The app's own
/// categories keep Information and above; framework categories keep only Warning and above, so ASP.NET request chatter
/// does not push the messages worth reading out of the buffer.
/// </summary>
internal sealed class BufferedLoggerProvider(LogBuffer buffer, TimeProvider timeProvider) : ILoggerProvider
{
    private const string AppCategoryPrefix = "KnowledgeBase.";
    private const int MaxMessageLength = 4000;
    private const int MaxExceptionLength = 8000;

    public ILogger CreateLogger(string categoryName) => new BufferedLogger(categoryName, buffer, timeProvider);

    public void Dispose()
    {
    }

    private sealed class BufferedLogger(string category, LogBuffer buffer, TimeProvider timeProvider) : ILogger
    {
        private readonly LogLevel _minimumLevel = category.StartsWith(AppCategoryPrefix, StringComparison.Ordinal)
            ? LogLevel.Information
            : LogLevel.Warning;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            // Recording a log must never fail the code that logged, so a formatter that throws is left out of the buffer.
            string message;
            try
            {
                message = formatter(state, exception);
            }
            catch (Exception)
            {
                return;
            }

            buffer.Add(
                timeProvider.GetUtcNow(),
                logLevel,
                category,
                Truncate(message, MaxMessageLength),
                exception is null ? null : Truncate(exception.ToString(), MaxExceptionLength));
        }

        private static string Truncate(string text, int maxLength) =>
            text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "…");
    }
}
