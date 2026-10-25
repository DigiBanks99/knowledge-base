namespace KnowledgeBase.Agent.Research.Errors;

internal sealed class ResearchStoreError(string filePath, string reason)
    : Error("ResearchStoreFailed", $"Failed to read or write '{filePath}': {reason}")
{
    public string FilePath { get; } = filePath;
}
