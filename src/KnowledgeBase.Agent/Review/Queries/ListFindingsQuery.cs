using System.Text.Json;

using KnowledgeBase.Agent.Common;
using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Review.Models;
using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Errors;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Review.Queries;

public record ListFindingsQuery(FindingState DesiredState);

internal sealed class ListFindingsQueryHandler(
    ILogger<ListFindingsQueryHandler> logger,
    IOptions<WikiConfigurationSettings> options,
    ListFilesHandler listFilesHandler,
    ReadFileHandler readFileHandler)
{
    private readonly WikiConfigurationSettings _settings = options.Value;

    public async Task<Result<IReadOnlyCollection<ReviewFinding>, Error>> Handle(
        ListFindingsQuery request,
        CancellationToken cancellationToken)
    {
        string findingDirectory = request.DesiredState switch
        {
            FindingState.Open => _settings.ReviewOpenFindingsDirectory,
            FindingState.Closed => _settings.ReviewClosedFindingsDirectory,
            _ => throw new InvalidOperationException($"Unknown finding state: {request.DesiredState}")
        };

        return await listFilesHandler
            .Handle(new ListFilesRequest(findingDirectory))
            .Bind(files => ReadAllFindings(files, cancellationToken));
    }

    private async Task<Result<IReadOnlyCollection<ReviewFinding>, Error>> ReadAllFindings(
        IEnumerable<string> files,
        CancellationToken cancellationToken) => await files
            .Select(filePath => ReadFindings(filePath, cancellationToken))
            .ZipResultsAsync();

    private async Task<Result<IReadOnlyCollection<ReviewFinding>, Error>> ReadFindings(
        string filePath,
        CancellationToken cancellationToken) => await readFileHandler
        .Handle(new ReadFileRequest(filePath), cancellationToken)
        .Bind(ReadFindingContent)
        .Map(IReadOnlyCollection<ReviewFinding> (finding) => [finding])
        .OnFailureCompensate(error => SkipUnreadableFinding(filePath, error));

    /// <summary>
    /// A finding that is corrupt (such as one edited by hand), or that was resolved and removed while the findings were
    /// being listed, is skipped, so it cannot hide every other finding from the UI and the Editor.
    /// </summary>
    private Result<IReadOnlyCollection<ReviewFinding>, Error> SkipUnreadableFinding(string filePath, Error error)
    {
        if (error is not (SerializationError or FileNotFoundError))
        {
            return error;
        }

        logger.SkippedUnreadableFinding(filePath, error);
        return Result.Success<IReadOnlyCollection<ReviewFinding>, Error>([]);
    }

    private static Result<ReviewFinding, Error> ReadFindingContent(string content) => Result
        .Try<ReviewFinding, Error>(
            () => JsonSerializer.Deserialize<ReviewFinding>(content, JsonSerializerOptions.Web)
                  ?? throw new JsonException("Deserialized object is null."),
            _ => new SerializationError(content, typeof(ReviewFinding)));
}