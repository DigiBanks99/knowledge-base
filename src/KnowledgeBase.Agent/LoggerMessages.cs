namespace KnowledgeBase.Agent;

internal static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Error, "An error occurred: {Error}")]
    public static partial void ErrorOccurred(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Warning, "Watcher for '{Directory}' reported an error; rescanning the directory.")]
    public static partial void DirectoryWatcherFailed(this ILogger logger, string directory, Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to handle '{FilePath}'.")]
    public static partial void DirectoryWatcherHandlerFailed(this ILogger logger, string filePath, Exception exception);

    [LoggerMessage(LogLevel.Warning, "'{FilePath}' is still being written; it will be picked up on the next start or rescan.")]
    public static partial void FileNotReady(this ILogger logger, string filePath);
}
