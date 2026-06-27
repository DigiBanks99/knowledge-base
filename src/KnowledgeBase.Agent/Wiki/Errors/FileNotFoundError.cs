namespace KnowledgeBase.Agent.Wiki.Errors;

internal sealed class FileNotFoundError(string filePath)
    : Error("FileNotFound", $"File '{filePath}' not found")
{
    public string FilePath { get; } = filePath;
}