using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Files;

public record DeleteDirectoryRequest(string DirectoryPath);

/// <summary>
/// Deletes an empty directory. Never recursive: whatever is inside is moved or deleted first, one tool call per file,
/// so a run's record shows every page that left the directory.
/// </summary>
internal sealed class DeleteDirectoryHandler(ILogger<DeleteDirectoryHandler> logger)
{
    public Result<bool, Error> Handle(DeleteDirectoryRequest request)
    {
        DirectoryInfo directory = new(request.DirectoryPath);
        if (!directory.Exists)
        {
            logger.DirectoryNotFound(directory.FullName);
            return DirectoryNotFoundError.Create(directory);
        }

        string[] entries = [.. directory
            .EnumerateFileSystemInfos()
            .Select(entry => entry is DirectoryInfo ? $"{entry.Name}/" : entry.Name)
            .Order(StringComparer.Ordinal)];
        if (entries.Length > 0)
        {
            return new DirectoryNotEmptyError(directory.FullName, entries);
        }

        try
        {
            logger.DeletingDirectory(directory.FullName);
            directory.Delete(recursive: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.FailedToDeleteDirectory(directory.FullName, ex);
            return new DeleteDirectoryError(directory.FullName, ex.Message);
        }
    }
}
