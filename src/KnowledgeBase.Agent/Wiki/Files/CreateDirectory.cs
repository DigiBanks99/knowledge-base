using KnowledgeBase.Agent.Wiki.Errors;

namespace KnowledgeBase.Agent.Wiki.Files;

public record CreateDirectory(DirectoryInfo DirectoryInfo)
{
    public static CreateDirectory Create(string parentDirectory, string directoryName) =>
        new(new DirectoryInfo(Path.Combine(parentDirectory, directoryName)));

    public static CreateDirectory Create(string directoryPath) =>
        new(new DirectoryInfo(directoryPath));
}

internal sealed class CreateDirectoryHandler(ILogger<CreateDirectoryHandler> logger)
{
    public Result<string, Error> Handle(CreateDirectory request)
    {
        if (request.DirectoryInfo.Exists)
        {
            return request.DirectoryInfo.FullName;
        }

        try
        {
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