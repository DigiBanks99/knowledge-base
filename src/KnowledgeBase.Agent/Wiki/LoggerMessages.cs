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

    [LoggerMessage(LogLevel.Error, "Failed to move file '{SourcePath}' to '{DestinationPath}'")]
    public static partial void FailedToMoveFile(this ILogger logger, string sourcePath, string destinationPath, Exception exception);

    [LoggerMessage(LogLevel.Debug, "Serializing facts to file '{FilePath}'")]
    public static partial void SerializingFactsToFile(this ILogger logger, string filePath);

    [LoggerMessage(LogLevel.Debug, "New note created at '{FilePath}'")]
    public static partial void NewNoteCreated(this ILogger logger, string filePath);

    [LoggerMessage(LogLevel.Error, "Failed to process new note '{FilePath}'. Error: {Error}")]
    public static partial void FailedToProcessNewNote(this ILogger logger, string filePath, Error error);
}