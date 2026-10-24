namespace KnowledgeBase.Agent.Files.Errors;

internal sealed class FileReadError(string filePath)
    : Error("FileReadFailed", $"Failed to read file '{filePath}'")
{
    public string FilePath { get; } = filePath;
}