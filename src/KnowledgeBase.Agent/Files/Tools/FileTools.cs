using System.ComponentModel;
using System.Net.Mime;

using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Files.Tools;

internal sealed class FileTools(
    ILogger<FileTools> logger,
    IOptions<WikiConfigurationSettings> settings,
    CreateDirectoryHandler createDirectoryHandler,
    CreateFileHandler createFileHandler,
    DeleteFileHandler deleteFileHandler,
    ListFilesHandler listFilesHandler,
    MoveFileHandler moveFileHandler,
    ReadFileHandler readFileHandler,
    PatchFileHandler patchFileHandler)
{
    private readonly WikiConfigurationSettings _settings = settings.Value;

    [Description("Creates the specified directory at the given path in the wiki.")]
    public async Task<string> CreateDirectory(
        [Description("The path of the directory to be created, relative to the wiki root.")]
        string desiredDirectoryPath)
    {
        Result<string, Error> result = createDirectoryHandler
            .Handle(CreateDirectoryRequest.Create(ResolveWikiPath(desiredDirectoryPath)))
            .Map(ToWikiRelativePath)
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
        Result<string, Error> result = await createFileHandler
            .Handle(new CreateFileRequest(ResolveWikiPath(desiredFilePath), content, mimeType), cancellationToken)
            .Map(ToWikiRelativePath)
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
        Result<bool, Error> result = await deleteFileHandler
            .Handle(new DeleteFileRequest(ResolveWikiPath(desiredFilePath)))
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
            .Handle(new ListFilesRequest(ResolveWikiPath(desiredDirectoryPath), IncludeDirectories: true))
            .Map(paths => paths.Select(ToWikiRelativePath))
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
        string destinationFilePath)
    {
        Result<string, Error> result = moveFileHandler
            .Handle(new MoveFileRequest(ResolveWikiPath(sourceFilePath), ResolveWikiPath(destinationFilePath)))
            .Map(ToWikiRelativePath)
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
        Result<string, Error> result = await patchFileHandler
            .Handle(new PatchFileRequest(ResolveWikiPath(filePath), originalContent, patchedContent), cancellationToken)
            .Map(ToWikiRelativePath)
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
            .Handle(new ReadFileRequest(ResolveWikiPath(filePath)), cancellationToken)
            .TapError(logger.ErrorOccurred);
        return result.Match(
            success => success,
            error => throw new InvalidOperationException($"Failed to read file '{filePath}': {error.Message}"));
    }

    private string WikiRoot => Path.GetFullPath(_settings.WikiDirectory);

    private string ResolveWikiPath(string path)
    {
        string fullPath = Path.GetFullPath(path, WikiRoot);
        bool isInsideWiki = fullPath.Equals(WikiRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(Path.TrimEndingDirectorySeparator(WikiRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        return isInsideWiki
            ? fullPath
            : throw new InvalidOperationException($"The path '{path}' is outside the wiki. All paths must be relative to the wiki root.");
    }

    private string ToWikiRelativePath(string fullPath)
    {
        string relativePath = Path.GetRelativePath(WikiRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        return Directory.Exists(fullPath) ? $"{relativePath}/" : relativePath;
    }
}
