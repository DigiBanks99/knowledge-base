using KnowledgeBase.Agent.Wiki.Errors;

namespace KnowledgeBase.Agent.Wiki.Files;

public record MoveFileRequest(string SourcePath, string DestinationPath)
{
    public FileInfo SourceFileInfo => new(SourcePath);
    public FileInfo DestinationFileInfo => new(DestinationPath);
}

internal sealed class MoveFileHandler(ILogger<MoveFileHandler> logger)
{
    public Result<string, Error> Handle(MoveFileRequest request)
    {
        if (!request.SourceFileInfo.Exists)
        {
            return new FileNotFoundError(request.SourcePath);
        }

        try
        {
            request.SourceFileInfo.MoveTo(request.DestinationPath);
            return request.DestinationPath;
        }
        catch (IOException ex)
        {
            logger.FailedToMoveFile(request.SourcePath, request.DestinationPath, ex);
            return new FileMoveError(request.SourcePath, request.DestinationPath);
        }
    }
}