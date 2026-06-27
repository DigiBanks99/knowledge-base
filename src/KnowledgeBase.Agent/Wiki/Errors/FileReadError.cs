namespace KnowledgeBase.Agent.Wiki.Errors;

internal sealed class FileReadError(string filePath)
    : Error("FileReadFailed", $"Failed to read file '{filePath}'")
{
    public string FilePath { get; } = filePath;
}