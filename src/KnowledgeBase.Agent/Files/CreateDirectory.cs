using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Files;

public record CreateDirectoryRequest(DirectoryInfo DirectoryInfo)
{
    public static CreateDirectoryRequest Create(string parentDirectory, string directoryName) =>
        new(new DirectoryInfo(Path.Combine(parentDirectory, directoryName)));

    public static CreateDirectoryRequest Create(string directoryPath) =>
        new(new DirectoryInfo(directoryPath));
}

internal sealed class CreateDirectoryHandler(ILogger<CreateDirectoryHandler> logger)
{
    public Result<string, Error> Handle(CreateDirectoryRequest request)
    {
        if (request.DirectoryInfo.Exists)
        {
            return request.DirectoryInfo.FullName;
        }

        try
        {
            logger.CreatingDirectory(request.DirectoryInfo.FullName);
            Directory.CreateDirectory(request.DirectoryInfo.FullName);
            return request.DirectoryInfo.FullName;
        }
        catch (IOException ex)
        {
            logger.FailedToCreateDirectory(request.DirectoryInfo.FullName, ex);
            return new DirectoryCreateError(request.DirectoryInfo.FullName);
        }
    }
}