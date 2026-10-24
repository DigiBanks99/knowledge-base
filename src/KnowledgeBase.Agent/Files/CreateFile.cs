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

        try
        {
            logger.CreatingFile(filePath);
            await File.WriteAllTextAsync(filePath, request.Content, cancellationToken);
        }
        catch (IOException ex)
        {
            logger.CreateFileFailed(filePath, ex);
            return new FileWriteError(filePath);
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