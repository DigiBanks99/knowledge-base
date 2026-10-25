namespace KnowledgeBase.Agent.Wiki;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Information, "Initializing wiki structure...")]
    public static partial void InitializingWikiStructure(this ILogger logger);

    [LoggerMessage(LogLevel.Critical, "Failed to initialize wiki structure: {Error}")]
    public static partial void FailedToInitializeWikiStructure(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Information, "Initializing the wiki repository in '{Directory}'...")]
    public static partial void InitializingWikiRepository(this ILogger logger, string directory);

    [LoggerMessage(LogLevel.Error, "Failed to initialize the wiki repository; wiki changes will not be versioned: {Error}")]
    public static partial void FailedToInitializeWikiRepository(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Information, "Committed the wiki changes of {Agent} run '{RunId}'.")]
    public static partial void CommittedWikiRun(this ILogger logger, string agent, string runId);

    [LoggerMessage(LogLevel.Debug, "{Agent} run '{RunId}' changed nothing in the wiki; nothing to commit.")]
    public static partial void NothingToCommitForWikiRun(this ILogger logger, string agent, string runId);

    [LoggerMessage(LogLevel.Error, "Failed to commit the wiki changes of {Agent} run '{RunId}': {Error}")]
    public static partial void FailedToCommitWikiRun(this ILogger logger, string agent, string runId, Error error);

    [LoggerMessage(LogLevel.Information, "Pushed the wiki to the backup remote.")]
    public static partial void PushedWiki(this ILogger logger);

    [LoggerMessage(LogLevel.Warning, "Failed to push the wiki to the backup remote; the next commit retries it: {Error}")]
    public static partial void FailedToPushWiki(this ILogger logger, Error error);
}
