using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Files;

public record DeleteFileRequest(string FilePath);

internal sealed class DeleteFileHandler(ILogger<DeleteFileHandler> logger)
{
    public async Task<Result<bool, Error>> Handle(DeleteFileRequest request)
    {
        try
        {
            logger.DeletingFile(request.FilePath);
            File.Delete(request.FilePath);
            return true;
        }
        catch (Exception ex)
        {
            logger.FailedToDeleteFile(request.FilePath, ex);
            return new DeleteFileError(request.FilePath, ex.Message);
        }
    }
}
