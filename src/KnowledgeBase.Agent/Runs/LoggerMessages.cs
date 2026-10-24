namespace KnowledgeBase.Agent.Runs;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Error, "{Agent} run failed.")]
    public static partial void AgentRunFailed(this ILogger logger, string agent, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Failed to save the run record.")]
    public static partial void FailedToSaveRun(this ILogger logger, Exception exception);
}
