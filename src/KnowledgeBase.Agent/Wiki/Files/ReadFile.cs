using KnowledgeBase.Agent.Wiki.Errors;

namespace KnowledgeBase.Agent.Wiki.Files;

public record ReadFileRequest(string Path);

internal sealed class ReadFileHandler(ILogger<ReadFileHandler> logger)
{
    public async Task<Result<string, Error>> Handle(
        ReadFileRequest request,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.Path))
        {
            return new FileNotFoundError(request.Path);
        }

        try
        {
            string content = await File.ReadAllTextAsync(request.Path, cancellationToken);
            return content;
        }
        catch (IOException ex)
        {
            logger.ReadFileFailed(request.Path, ex);
            return new FileReadError(request.Path);
        }
    }
}