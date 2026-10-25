namespace KnowledgeBase.Agent.Files.Errors;

internal sealed class DirectoryReadError(string directory)
    : Error("DirectoryReadFailed", $"Failed to read files from directory '{directory}'.")
{
    public DirectoryInfo Directory { get; } = new(directory);
    public static DirectoryReadError Create(DirectoryInfo directory) => new(directory.FullName);
}
