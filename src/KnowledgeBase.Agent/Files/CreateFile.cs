using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Files;

public record CreateFileRequest(
    string Name,
    string Content,
    string MimeType);

internal sealed class CreateFileHandler(
    ILogger<CreateFileHandler> logger,
    CreateDirectoryHandler directoryHandler)
{
    public async Task<Result<string, Error>> Handle(
        CreateFileRequest request,
        CancellationToken cancellationToken) => await ValidateRequest(request)
            .Bind(CheckFileExists)
            .Bind(f => CreateFile(f, cancellationToken))
            .TapError(logger.CreateFileFailed);

    private static Result<CreateFileRequest, Error> CheckFileExists(
        CreateFileRequest request) => File.Exists(request.Name)
            ? new FileConflictError("FileExists", $"A file with the name '{request.Name}' already exists.")
            : request;

    private async Task<Result<string, Error>> CreateFile(
        CreateFileRequest request,
        CancellationToken cancellationToken)
    {
        string extension = request.MimeType switch
        {
            "text/plain" => ".txt",
            "text/markdown" => ".md",
            "text/log" => ".log",
            "application/json" => ".json",
            _ => throw new ArgumentException($"Unsupported MIME type: {request.MimeType}")
        };

        string filePath = Path.ChangeExtension(request.Name, extension);
        CreateDirectoryRequest createDirectory = CreateDirectoryRequest.Create(Path.GetDirectoryName(filePath)!);
        Result<string, Error> directoryResult = directoryHandler.Handle(createDirectory);
        if (directoryResult.IsFailure)
        {
            return directoryResult.Error;
        }

        // The file is written under a temporary name and then moved into place, so the pipeline's watchers never see it
        // before its content is complete. On Linux a watcher can otherwise open a new file between its creation and the
        // writer locking it, and read it empty.
        string temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            logger.CreatingFile(filePath);
            await File.WriteAllTextAsync(temporaryPath, request.Content, cancellationToken);
            File.Move(temporaryPath, filePath);
        }
        catch (IOException ex)
        {
            logger.CreateFileFailed(filePath, ex);
            return new FileWriteError(filePath);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        return filePath;
    }

    private static Result<CreateFileRequest, Error> ValidateRequest(CreateFileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new ValidationError("NameRequired", "The file name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return new ValidationError("ContentRequired", "The file content is required.");
        }

        if (string.IsNullOrWhiteSpace(request.MimeType))
        {
            return new ValidationError("MimeTypeRequired", "The file MIME type is required.");
        }

        return request;
    }
}