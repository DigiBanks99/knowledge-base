using KnowledgeBase.Agent;

namespace KnowledgeBase.Agent.Wiki;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Error, "Failed to create file: {Error}")]
    public static partial void CreateFileFailed(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Error, "Failed to create file '{FilePath}'")]
    public static partial void CreateFileFailed(this ILogger logger, string filePath, Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to create directory '{Directory}'")]
    public static partial void FailedToCreateDirectory(this ILogger logger, string directory, Exception exception);
}