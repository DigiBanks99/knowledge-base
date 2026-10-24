namespace KnowledgeBase.Agent.Events;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Warning, "The change watcher reported an error; notifying all areas.")]
    public static partial void ChangeWatcherFailed(this ILogger logger, Exception exception);
}