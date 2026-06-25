namespace KnowledgeBase.Agent.Wiki.Errors;

internal sealed class DirectoryCreateError(string directory)
    : Error("DirectoryCreationFailed", $"Failed to create directory '{directory}'.")
{
    public DirectoryInfo Directory { get; } = new(directory);
    public static DirectoryCreateError Create(DirectoryInfo directory) => new(directory.FullName);
}