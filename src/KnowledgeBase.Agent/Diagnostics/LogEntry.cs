using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.Diagnostics;

/// <summary>One log message the API kept, as the Logs page shows it.</summary>
/// <param name="Sequence">Counts every entry since start, so the page can tell entries apart and keep their order.</param>
/// <param name="At">When the message was logged.</param>
/// <param name="Level">How serious the message is.</param>
/// <param name="Category">The logger that wrote it, usually the class name, e.g. KnowledgeBase.Agent.Research.Search.TopicSearch.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception with its stack trace, when the message came with one.</param>
public sealed record LogEntry(
    long Sequence,
    DateTimeOffset At,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception);
