namespace KnowledgeBase.Agent.Files;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Information, "Creating file '{FilePath}'")]
    public static partial void CreatingFile(this ILogger logger, string filePath);

    [LoggerMessage(LogLevel.Error, "Failed to create file: {Error}")]
    public static partial void CreateFileFailed(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Error, "Failed to create file '{FilePath}'")]
    public static partial void CreateFileFailed(this ILogger logger, string filePath, Exception exception);

    [LoggerMessage(LogLevel.Information, "Creating directory '{Directory}'")]
    public static partial void CreatingDirectory(this ILogger logger, string directory);

    [LoggerMessage(LogLevel.Error, "Failed to create directory '{Directory}'")]
    public static partial void FailedToCreateDirectory(this ILogger logger, string directory, Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to read file '{FilePath}': {Error}")]
    public static partial void FailedToReadFile(this ILogger logger, string filePath, Error error);

    [LoggerMessage(LogLevel.Error, "Failed to read file '{FilePath}'")]
    public static partial void ReadFileFailed(this ILogger logger, string filePath, Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to read directory files '{Directory}'")]
    public static partial void FailedToReadDirectoryFiles(this ILogger logger, string directory, Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to move file '{SourcePath}' to '{DestinationPath}'")]
    public static partial void FailedToMoveFile(this ILogger logger, string sourcePath, string destinationPath, Exception exception);

    [LoggerMessage(LogLevel.Information, "Deleting file '{FilePath}'")]
    public static partial void DeletingFile(this ILogger logger, string filePath);

    [LoggerMessage(LogLevel.Error, "Failed to delete file '{FilePath}'")]
    public static partial void FailedToDeleteFile(this ILogger logger, string filePath, Exception exception);

    [LoggerMessage(LogLevel.Error, "Directory not found: '{DirectoryPath}'")]
    public static partial void DirectoryNotFound(this ILogger logger, string directoryPath);

    [LoggerMessage(LogLevel.Information, "Listing files in directory '{DirectoryPath}'")]
    public static partial void ListingFilesInDirectory(this ILogger logger, string directoryPath);

    [LoggerMessage(LogLevel.Error, "File not found: '{FilePath}'")]
    public static partial void FileNotFound(this ILogger logger, string filePath);
}
