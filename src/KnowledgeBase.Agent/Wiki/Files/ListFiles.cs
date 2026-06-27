using KnowledgeBase.Agent.Wiki.Errors;

namespace KnowledgeBase.Agent.Wiki.Files;

public record ListFilesRequest(string DirectoryPath);

internal sealed class ListFilesHandler(ILogger<ListFilesHandler> logger)
{
    public Result<IEnumerable<string>, Error> Handle(ListFilesRequest request)
    {
        if (!Directory.Exists(request.DirectoryPath))
        {
            return new DirectoryNotFoundError(request.DirectoryPath);
        }

        try
        {
            return Directory.GetFiles(request.DirectoryPath);
        }
        catch (IOException ex)
        {
            logger.FailedToReadDirectoryFiles(request.DirectoryPath, ex);
            return new DirectoryReadError(request.DirectoryPath);
        }
    }
}