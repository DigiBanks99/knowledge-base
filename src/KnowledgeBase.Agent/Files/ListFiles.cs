using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Files;

public record ListFilesRequest(string DirectoryPath, bool IncludeDirectories = false);

internal sealed class ListFilesHandler(ILogger<ListFilesHandler> logger)
{
    public Result<IEnumerable<string>, Error> Handle(ListFilesRequest request)
    {
        if (!Directory.Exists(request.DirectoryPath))
        {
            logger.DirectoryNotFound(request.DirectoryPath);
            return new DirectoryNotFoundError(request.DirectoryPath);
        }

        try
        {
            logger.ListingFilesInDirectory(request.DirectoryPath);
            string[] files = Directory.GetFiles(request.DirectoryPath);
            return request.IncludeDirectories
                ? [.. Directory.GetDirectories(request.DirectoryPath), .. files]
                : files;
        }
        catch (IOException ex)
        {
            logger.FailedToReadDirectoryFiles(request.DirectoryPath, ex);
            return new DirectoryReadError(request.DirectoryPath);
        }
    }
}
