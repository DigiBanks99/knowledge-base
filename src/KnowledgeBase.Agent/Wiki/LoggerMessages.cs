namespace KnowledgeBase.Agent.Wiki;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Information, "Initializing wiki structure...")]
    public static partial void InitializingWikiStructure(this ILogger logger);

    [LoggerMessage(LogLevel.Critical, "Failed to initialize wiki structure: {Error}")]
    public static partial void FailedToInitializeWikiStructure(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Error, "Failed to create file: {Error}")]
    public static partial void CreateFileFailed(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Error, "Failed to create file '{FilePath}'")]
    public static partial void CreateFileFailed(this ILogger logger, string filePath, Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to create directory '{Directory}'")]
    public static partial void FailedToCreateDirectory(this ILogger logger, string directory, Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to read file '{FilePath}'")]
    public static partial void ReadFileFailed(this ILogger logger, string filePath, Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to read directory files '{Directory}'")]
    public static partial void FailedToReadDirectoryFiles(this ILogger logger, string directory, Exception exception);
}