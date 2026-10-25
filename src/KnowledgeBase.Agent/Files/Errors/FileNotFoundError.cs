namespace KnowledgeBase.Agent.Files.Errors;

internal sealed class FileNotFoundError(string filePath)
    : Error("FileNotFound", $"File '{filePath}' not found")
{
    public string FilePath { get; } = filePath;
}
