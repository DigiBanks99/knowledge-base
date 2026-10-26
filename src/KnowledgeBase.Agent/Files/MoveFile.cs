using KnowledgeBase.Agent.Common;
using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Files;

public record MoveFileRequest(string SourcePath, string DestinationPath)
{
    public FileInfo SourceFileInfo => new(SourcePath);
    public FileInfo DestinationFileInfo => new(DestinationPath);
}

/// <remarks>
/// Moving a file is how the pipeline claims one that just arrived, so a move refused because the file is still open
/// elsewhere, such as by a virus scanner, is retried rather than leaving the file unclaimed until a restart.
/// </remarks>
internal sealed class MoveFileHandler(ILogger<MoveFileHandler> logger)
{
    public async Task<Result<string, Error>> Handle(MoveFileRequest request, CancellationToken cancellationToken)
    {
        if (!request.SourceFileInfo.Exists)
        {
            logger.FileNotFound(request.SourcePath);
            return new FileNotFoundError(request.SourcePath);
        }

        try
        {
            await FileInUseRetry.RunAsync(() => request.SourceFileInfo.MoveTo(request.DestinationPath), cancellationToken);
            return request.DestinationPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.FailedToMoveFile(request.SourcePath, request.DestinationPath, ex);
            return new FileMoveError(request.SourcePath, request.DestinationPath);
        }
    }
}
