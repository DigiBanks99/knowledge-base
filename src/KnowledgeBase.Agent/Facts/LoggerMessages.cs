namespace KnowledgeBase.Agent.Facts;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Debug, "Serializing facts to file '{FilePath}'")]
    public static partial void SerializingFactsToFile(this ILogger logger, string filePath);

    [LoggerMessage(LogLevel.Debug, "New fact created at '{FilePath}'")]
    public static partial void NewFactCreated(this ILogger logger, string filePath);

    [LoggerMessage(LogLevel.Error, "Failed to ingest new fact '{FilePath}'. Error: {Error}")]
    public static partial void FailedToIngestNewFact(this ILogger logger, string filePath, Error error);

    [LoggerMessage(LogLevel.Debug, "Fact finder finish reason: {FinishReason}")]
    public static partial void FactFinderFinishReason(this ILogger logger, string finishReason);
}
