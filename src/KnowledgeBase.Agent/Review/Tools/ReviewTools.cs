using System.ComponentModel;
using System.Net.Mime;
using System.Text.Json;

using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Review.Models;
using KnowledgeBase.Agent.Review.Queries;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Review.Tools;

internal sealed class ReviewTools(
    ILogger<ReviewTools> logger,
    IOptions<WikiConfigurationSettings> options,
    TimeProvider timeProvider,
    CreateFileHandler createFileHandler,
    DeleteFileHandler deleteFileHandler,
    ListFindingsQueryHandler listFindingsQueryHandler)
{
    private readonly WikiConfigurationSettings _settings = options.Value;

    [Description(
        """
        Records the findings of a review of the wiki. Call this once, after completing the full review, with a summary
        of the review and every finding. Each finding is stored as an open finding for the Editor to resolve.
        """)]
    public async Task<ReviewWikiResult> WriteFindings(
        [Description("The summary of the review: the overall state of the wiki and the most important themes in the findings.")]
        string summary,
        [Description("The findings of the review. Do not repeat findings that are already open.")]
        IEnumerable<NewReviewFinding> findings,
        CancellationToken cancellationToken)
    {
        DateTimeOffset reviewDate = timeProvider.GetUtcNow();
        string reviewId = $"review-{reviewDate:yyyy-MM-dd-HH-mm-ss}";
        ReviewFinding[] reviewFindings = findings
            .Select((finding, index) => new ReviewFinding(
                $"{reviewId}-{index + 1:000}",
                reviewDate,
                finding.PagePath,
                finding.Finding,
                finding.Recommendation,
                finding.Priority))
            .ToArray();

        foreach (ReviewFinding finding in reviewFindings)
        {
            await WriteJson(Path.Combine(_settings.ReviewOpenFindingsDirectory, $"{finding.Id}.json"), finding, cancellationToken);
        }

        ReviewWikiResult reviewResult = new(reviewDate, summary, [.. reviewFindings.Select(finding => finding.Id)]);
        await WriteJson(Path.Combine(_settings.ReviewReportsDirectory, $"{reviewId}.json"), reviewResult, cancellationToken);

        return reviewResult;
    }

    [Description("Lists the open findings of previous reviews of the wiki that have not been resolved yet.")]
    public async Task<IReadOnlyCollection<ReviewFinding>> ListFindings(CancellationToken cancellationToken)
    {
        ListFindingsQuery listFindingsQuery = new(FindingState.Open);
        Result<IReadOnlyCollection<ReviewFinding>, Error> result = await listFindingsQueryHandler
            .Handle(listFindingsQuery, cancellationToken)
            .TapError(logger.ErrorOccurred);
        return result.IsFailure
            ? throw new InvalidOperationException("Failed to list review findings.")
            : result.Value;
    }

    [Description(
        """
        Resolves open findings once the wiki has been edited to address them. Each resolution needs the finding id and a
        short description of what was changed (or why no change was needed). Resolved findings are closed.
        """)]
    public async Task<IReadOnlyCollection<string>> ResolveFindings(
        [Description("The resolutions of the findings that have been addressed.")]
        IReadOnlyCollection<FindingResolution> resolutions,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyCollection<ReviewFinding>, Error> openFindings = await listFindingsQueryHandler
            .Handle(new ListFindingsQuery(FindingState.Open), cancellationToken);
        if (openFindings.IsFailure)
        {
            logger.ErrorOccurred(openFindings.Error);
            throw new InvalidOperationException($"Failed to read open findings: {openFindings.Error.Message}");
        }

        DateTimeOffset resolvedAt = timeProvider.GetUtcNow();
        Dictionary<string, ReviewFinding> findingsById = openFindings.Value.ToDictionary(finding => finding.Id);
        List<string> resolved = [];
        List<string> unknown = [];
        foreach (FindingResolution resolution in resolutions)
        {
            if (!findingsById.TryGetValue(resolution.Id, out ReviewFinding? finding))
            {
                unknown.Add(resolution.Id);
                continue;
            }

            ReviewFinding closedFinding = finding with { Resolution = resolution.Resolution, ResolvedAt = resolvedAt };
            await WriteJson(Path.Combine(_settings.ReviewClosedFindingsDirectory, $"{finding.Id}.json"), closedFinding, cancellationToken);
            await deleteFileHandler
                .Handle(new DeleteFileRequest(Path.Combine(_settings.ReviewOpenFindingsDirectory, $"{finding.Id}.json")))
                .TapError(logger.ErrorOccurred);
            resolved.Add(finding.Id);
        }

        logger.ResolvedFindings(resolved.Count, resolvedAt);

        return unknown.Count > 0
            ? throw new InvalidOperationException($"Resolved {resolved.Count} findings, but these ids are not open findings: {string.Join(", ", unknown)}")
            : resolved;
    }

    private async Task WriteJson<T>(string filePath, T content, CancellationToken cancellationToken)
    {
        logger.SerializingReviewFindingsToFile(filePath);

        CreateFileRequest createFileRequest = new(
            filePath,
            JsonSerializer.Serialize(content, JsonSerializerOptions.Web),
            MediaTypeNames.Application.Json);
        Result<string, Error> result = await createFileHandler
            .Handle(createFileRequest, cancellationToken)
            .TapError(logger.ErrorOccurred);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to write '{Path.GetFileName(filePath)}': {result.Error.Message}");
        }
    }
}
