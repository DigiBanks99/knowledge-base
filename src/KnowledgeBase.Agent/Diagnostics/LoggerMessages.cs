namespace KnowledgeBase.Agent.Diagnostics;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Information, "Restored {Count} recent log entries from the logs directory.")]
    public static partial void RestoredLogs(this ILogger logger, int count);

    [LoggerMessage(LogLevel.Warning, "Could not restore the recent log entries from the logs directory; the Logs page starts empty.")]
    public static partial void FailedToRestoreLogs(this ILogger logger, Exception exception);
}
