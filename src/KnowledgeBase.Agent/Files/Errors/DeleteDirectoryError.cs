namespace KnowledgeBase.Agent.Files.Errors;

internal sealed class DeleteDirectoryError(string directory, string reason)
    : Error("DirectoryDeleteFailed", $"Failed to delete directory '{directory}': {reason}")
{
    public string Directory { get; } = directory;
    public string Reason { get; } = reason;
}
