namespace KnowledgeBase.Agent.Wiki;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Information, "Initializing wiki structure...")]
    public static partial void InitializingWikiStructure(this ILogger logger);

    [LoggerMessage(LogLevel.Critical, "Failed to initialize wiki structure: {Error}")]
    public static partial void FailedToInitializeWikiStructure(this ILogger logger, Error error);
}