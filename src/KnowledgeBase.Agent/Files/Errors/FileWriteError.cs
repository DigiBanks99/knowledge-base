namespace KnowledgeBase.Agent.Files.Errors;

internal sealed class FileWriteError(string filePath)
    : Error("FileWriteFailed", $"Failed to write to file '{filePath}'")
{
    public string FilePath { get; } = filePath;
}
