using System.Globalization;

using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.Diagnostics;

/// <summary>
/// Sends every logger to the <see cref="LogBuffer"/> and the log file as well as to the console and OpenTelemetry. The
/// app's own categories keep Information and above; framework categories keep only Warning and above, so ASP.NET
/// request chatter does not fill the files or push the messages worth reading out of the buffer.
/// </summary>
internal sealed class BufferedLoggerProvider(LogBuffer buffer, LogFiles files, TimeProvider timeProvider) : ILoggerProvider
{
    private const string AppCategoryPrefix = "KnowledgeBase.";
    private const string AgentNamespacePrefix = "KnowledgeBase.Agent.";
    private const string FrameworkArea = "Framework";
    private const string AppArea = "App";
    private const string OriginalFormat = "{OriginalFormat}";
    private const int MaxMessageLength = 4000;
    private const int MaxExceptionLength = 8000;
    private const int MaxPropertyLength = 300;

    public ILogger CreateLogger(string categoryName) => new BufferedLogger(categoryName, buffer, files, timeProvider);

    public void Dispose()
    {
    }

    private sealed class BufferedLogger(string category, LogBuffer buffer, LogFiles files, TimeProvider timeProvider) : ILogger
    {
        private readonly LogLevel _minimumLevel = category.StartsWith(AppCategoryPrefix, StringComparison.Ordinal)
            ? LogLevel.Information
            : LogLevel.Warning;

        private readonly string _area = AreaOf(category);

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

            // Recording a log must never fail the code that logged, so a formatter that throws is left out.
            string message;
            try
            {
                message = formatter(state, exception);
            }
            catch (Exception)
            {
                return;
            }

            LogEntry entry = buffer.Add(
                timeProvider.GetUtcNow(),
                logLevel,
                _area,
                category,
                Truncate(message, MaxMessageLength),
                PropertiesOf(state),
                exception is null ? null : Truncate(exception.ToString(), MaxExceptionLength));
            files.Append(entry);
        }

        /// <summary>
        /// The values a source-generated log message was written with, by name (Topic, RunId, Url). They are what lets the
        /// Logs page find every entry about one topic or one run.
        /// </summary>
        private static Dictionary<string, string> PropertiesOf<TState>(TState state)
        {
            Dictionary<string, string> properties = new(StringComparer.Ordinal);
            if (state is not IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                return properties;
            }

            foreach ((string name, object? value) in pairs)
            {
                if (name == OriginalFormat || value is null)
                {
                    continue;
                }

                string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (text.Length > 0)
                {
                    properties[name] = Truncate(text, MaxPropertyLength);
                }
            }

            return properties;
        }

        private static string Truncate(string text, int maxLength) =>
            text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "…");
    }

    /// <summary>
    /// The feature folder a category belongs to: KnowledgeBase.Agent.Research.Search.TopicSearch is Research. Classes
    /// directly under KnowledgeBase.Agent are App, and everything else is Framework.
    /// </summary>
    private static string AreaOf(string category)
    {
        if (!category.StartsWith(AgentNamespacePrefix, StringComparison.Ordinal))
        {
            return category.StartsWith(AppCategoryPrefix, StringComparison.Ordinal) ? AppArea : FrameworkArea;
        }

        string remainder = category[AgentNamespacePrefix.Length..];
        int dot = remainder.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? AppArea : remainder[..dot];
    }
}
