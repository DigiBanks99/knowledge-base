using KnowledgeBase.Agent.Common;
using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Files;

public record ReadFileRequest(string Path);

/// <remarks>
/// Some watchers claim a file that just arrived by reading it first, so a read refused because the file is still open
/// elsewhere, such as by a virus scanner, is retried rather than failing the file.
/// </remarks>
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
            string content = await FileInUseRetry.RunAsync(
                () => File.ReadAllTextAsync(request.Path, cancellationToken),
                cancellationToken);
            return content;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.ReadFileFailed(request.Path, ex);
            return new FileReadError(request.Path);
        }
    }
}
