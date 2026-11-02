using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.Diagnostics;

/// <summary>One log message the API kept, as the Logs page shows it.</summary>
/// <param name="Sequence">Counts every entry since start, so the page can tell entries apart and keep their order.</param>
/// <param name="At">When the message was logged.</param>
/// <param name="Level">How serious the message is.</param>
/// <param name="Area">The part of the app that logged it, such as Research, Review or Runs; Framework for ASP.NET and other libraries.</param>
/// <param name="Category">The logger that wrote it, usually the class name, e.g. KnowledgeBase.Agent.Research.Search.TopicSearch.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Properties">The values the message was written with, such as Topic or RunId, so entries about the same topic or run can be found together.</param>
/// <param name="Exception">The exception with its stack trace, when the message came with one.</param>
public sealed record LogEntry(
    long Sequence,
    DateTimeOffset At,
    LogLevel Level,
    string Area,
    string Category,
    string Message,
    IReadOnlyDictionary<string, string> Properties,
    string? Exception);
