namespace KnowledgeBase.Agent.Files.Errors;

internal sealed class FileMoveError(string sourcePath, string destinationPath)
    : Error("FileMoveFailed", $"Failed to move file '{sourcePath}' to '{destinationPath}'")
{
    public string SourcePath { get; } = sourcePath;
    public string DestinationPath { get; } = destinationPath;
}
