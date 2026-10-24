namespace KnowledgeBase.Agent.Files.Errors;

internal sealed class DeleteFileError(string filePath, string reason)
    : Error("FileDeleteFailed", $"Failed to delete file '{filePath}': {reason}")
{
    public string FilePath { get; } = filePath;
    public string Reason { get; } = reason;
}