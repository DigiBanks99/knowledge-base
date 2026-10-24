namespace KnowledgeBase.Agent.Notes;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Debug, "New note created at '{FilePath}'")]
    public static partial void NewNoteCreated(this ILogger logger, string filePath);

    [LoggerMessage(LogLevel.Error, "Failed to process new note '{FilePath}'. Error: {Error}")]
    public static partial void FailedToProcessNewNote(this ILogger logger, string filePath, Error error);
}