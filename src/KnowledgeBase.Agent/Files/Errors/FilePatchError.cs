namespace KnowledgeBase.Agent.Files.Errors;

internal sealed class FilePatchError(string filePath, string reason)
    : Error("FilePatchFailed", $"Failed to patch file '{filePath}': {reason}")
{
    public string FilePath { get; } = filePath;
    public string Reason { get; } = reason;
}