namespace KnowledgeBase.Agent.Files.Errors;

internal sealed class DirectoryNotEmptyError(string directory, IReadOnlyList<string> entries)
    : Error(
        "DirectoryNotEmpty",
        $"Directory '{directory}' is not empty. It still contains: {string.Join(", ", entries)}. Move each page to where "
            + "it belongs with `MoveFile` or remove it with `DeleteFile`, then delete the directory.")
{
    public string Directory { get; } = directory;
    public IReadOnlyList<string> Entries { get; } = entries;
}
