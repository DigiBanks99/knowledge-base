using System.ComponentModel;
using System.Net.Mime;

using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Files.Tools;

internal sealed class FileTools(
    ILogger<FileTools> logger,
    WikiPaths wikiPaths,
    AgentRunTracker runTracker,
    CreateDirectoryHandler createDirectoryHandler,
    CreateFileHandler createFileHandler,
    DeleteFileHandler deleteFileHandler,
    ListFilesHandler listFilesHandler,
    MoveFileHandler moveFileHandler,
    ReadFileHandler readFileHandler,
    PatchFileHandler patchFileHandler)
{
    [Description("Creates the specified directory at the given path in the wiki.")]
    public async Task<string> CreateDirectory(
        [Description("The path of the directory to be created, relative to the wiki root.")]
        string desiredDirectoryPath)
    {
        string directoryPath = wikiPaths.Resolve(desiredDirectoryPath);
        EnsureAllowed(nameof(CreateDirectory), directoryPath);
        Result<string, Error> result = createDirectoryHandler
            .Handle(CreateDirectoryRequest.Create(directoryPath))
            .Map(wikiPaths.ToRelative)
            .TapError(logger.ErrorOccurred);
        return result.Match(
            success => success,
            error => throw new InvalidOperationException($"Failed to create directory '{desiredDirectoryPath}': {error.Message}"));
    }

    [Description("Creates a new file with the specified content at the given path in the wiki.")]
    public async Task<string> CreateFile(
        [Description("The path of the file to be created, relative to the wiki root.")]
        string desiredFilePath,
        [Description("The content to be written to the file.")]
        string content,
        [Description($"The MIME type of the file content. Defaults to '{MediaTypeNames.Text.Markdown}'.")]
        string mimeType = MediaTypeNames.Text.Markdown,
        CancellationToken cancellationToken = default)
    {
        string filePath = wikiPaths.Resolve(desiredFilePath);
        EnsureAllowed(nameof(CreateFile), filePath);
        Result<string, Error> result = await createFileHandler
            .Handle(new CreateFileRequest(filePath, content, mimeType), cancellationToken)
            .Map(wikiPaths.ToRelative)
            .TapError(logger.ErrorOccurred);
        return result.Match(
            success => success,
            error => throw new InvalidOperationException($"Failed to create file '{desiredFilePath}': {error.Message}"));
    }

    [Description("Deletes the specified file at the given path in the wiki.")]
    public async Task<bool> DeleteFile(
        [Description("The path of the file to be deleted, relative to the wiki root.")]
        string desiredFilePath)
    {
        string filePath = wikiPaths.Resolve(desiredFilePath);
        EnsureAllowed(nameof(DeleteFile), filePath);
        Result<bool, Error> result = await deleteFileHandler
            .Handle(new DeleteFileRequest(filePath))
            .TapError(logger.ErrorOccurred);
        return result.Match(
            success => success,
            error => throw new InvalidOperationException($"Failed to delete file '{desiredFilePath}': {error.Message}"));
    }

    [Description("Lists the sub-directories and files in the specified directory. Directories end with '/'. Use '.' for the wiki root.")]
    public IEnumerable<string> ListFiles(
        [Description("The path of the directory to list, relative to the wiki root.")]
        string desiredDirectoryPath)
    {
        Result<IEnumerable<string>, Error> result = listFilesHandler
            .Handle(new ListFilesRequest(wikiPaths.Resolve(desiredDirectoryPath), IncludeDirectories: true))
            .Map(paths => paths.Where(path => !wikiPaths.IsRepositoryPath(path)).Select(wikiPaths.ToRelative))
            .TapError(logger.ErrorOccurred);
        return result.Match(
            success => success,
            error => throw new InvalidOperationException($"Failed to list files in directory '{desiredDirectoryPath}': {error.Message}"));
    }

    [Description("Moves a file from the source path to the destination path.")]
    public async Task<string> MoveFile(
        [Description("The path of the file to be moved, relative to the wiki root.")]
        string sourceFilePath,
        [Description("The path of the destination where the file should be moved, relative to the wiki root.")]
        string destinationFilePath,
        CancellationToken cancellationToken = default)
    {
        string sourcePath = wikiPaths.Resolve(sourceFilePath);
        string destinationPath = wikiPaths.Resolve(destinationFilePath);
        EnsureAllowed(nameof(MoveFile), sourcePath, destinationPath);
        Result<string, Error> result = await moveFileHandler
            .Handle(new MoveFileRequest(sourcePath, destinationPath), cancellationToken)
            .Map(wikiPaths.ToRelative)
            .TapError(logger.ErrorOccurred);
        return result.Match(
            success => success,
            error => throw new InvalidOperationException($"Failed to move file from '{sourceFilePath}' to '{destinationFilePath}': {error.Message}"));
    }

    [Description("Patches a file by replacing the original content with the new content. The text is whitespace sensitive so must match exactly what is in the file.")]
    public async Task<string> PatchFile(
        [Description("The path of the file to be patched, relative to the wiki root.")]
        string filePath,
        [Description("The original content of the file. Whitespace sensitive.")]
        string originalContent,
        [Description("The new content to replace the original content. Whitespace sensitive.")]
        string patchedContent,
        CancellationToken cancellationToken = default)
    {
        string fullPath = wikiPaths.Resolve(filePath);
        EnsureAllowed(nameof(PatchFile), fullPath);
        Result<string, Error> result = await patchFileHandler
            .Handle(new PatchFileRequest(fullPath, originalContent, patchedContent), cancellationToken)
            .Map(wikiPaths.ToRelative)
            .TapError(logger.ErrorOccurred);
        return result.Match(
            success => success,
            error => throw new InvalidOperationException($"Failed to patch file '{filePath}': {error.Message}"));
    }

    [Description("Reads the content of a file at the specified path, relative to the wiki root.")]
    public async Task<string> ReadFile(
        [Description("The path of the file to be read, relative to the wiki root.")]
        string filePath,
        CancellationToken cancellationToken = default)
    {
        Result<string, Error> result = await readFileHandler
            .Handle(new ReadFileRequest(wikiPaths.Resolve(filePath)), cancellationToken)
            .TapError(logger.ErrorOccurred);
        return result.Match(
            success => success,
            error => throw new InvalidOperationException($"Failed to read file '{filePath}': {error.Message}"));
    }

    [Description(
        """
        Searches every wiki page for lines containing all of the given words (case-insensitive) and returns matches as
        'path:line: text'. Use it to find pages about a person, topic or term before reading them.
        """)]
    public async Task<IReadOnlyList<string>> SearchWiki(
        [Description("One or more words to search for, separated by spaces. A line matches when it contains all of them.")]
        string query,
        CancellationToken cancellationToken = default)
    {
        const int maxMatches = 50;
        string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return [];
        }

        List<string> matches = [];
        IEnumerable<string> pages = Directory
            .EnumerateFiles(wikiPaths.Root, "*.md", SearchOption.AllDirectories)
            .Where(filePath => !wikiPaths.IsRepositoryPath(filePath));
        foreach (string filePath in pages)
        {
            string[] lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            for (int index = 0; index < lines.Length && matches.Count < maxMatches; index++)
            {
                if (terms.All(term => lines[index].Contains(term, StringComparison.OrdinalIgnoreCase)))
                {
                    matches.Add($"{wikiPaths.ToRelative(filePath)}:{index + 1}: {lines[index].Trim()}");
                }
            }

            if (matches.Count >= maxMatches)
            {
                matches.Add($"(stopped after {maxMatches} matches; refine the query)");
                break;
            }
        }

        return matches;
    }

    /// <summary>Refuses a call the current run may not make (see <see cref="RunToolPolicy"/>) before anything changes.</summary>
    private void EnsureAllowed(string tool, params string[] fullPaths)
    {
        string? refusal = RunToolPolicy.Refusal(runTracker.CurrentOrigin, tool, [.. fullPaths.Select(wikiPaths.ToRelative)]);
        if (refusal is not null)
        {
            throw new InvalidOperationException(refusal);
        }
    }
}
