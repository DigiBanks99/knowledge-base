namespace KnowledgeBase.Agent.Wiki.Errors;

internal sealed class DirectoryNotFoundError(string directory)
    : Error("DirectoryNotFound", $"Directory '{directory}' not found.")
{
    public DirectoryInfo Directory { get; } = new(directory);
    public static DirectoryNotFoundError Create(DirectoryInfo directory) => new(directory.FullName);
}