using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>
/// The author's judgement of a result the Keeper judged, or none to clear it. It only feeds the Keeper's agreement
/// figures: the verdict, the batch and any research note stay as they are.
/// </summary>
/// <param name="Slug">The topic's slug, as the Topics page lists it.</param>
/// <param name="SearchedAt">When the search that found the result ran, which names its batch.</param>
public record MarkResultCommand(string Slug, DateTimeOffset SearchedAt, string Url, ResultMark? Mark);

internal sealed class MarkResultCommandHandler(
    ILogger<MarkResultCommandHandler> logger,
    IResearchStore store,
    WikiPaths wikiPaths)
{
    public async Task<Result<BatchResult, Error>> Handle(
        MarkResultCommand request,
        CancellationToken cancellationToken) => await TopicSlugs
        .Resolve(request.Slug, wikiPaths)
        // The JSON reader takes numbers for enums, so a mark that is neither is refused here.
        .Ensure(
            _ => request.Mark is null || Enum.IsDefined(request.Mark.Value),
            new ValidationError("InvalidMark", $"'{request.Mark}' is not a mark. A result is marked Useful or Noise, or null to clear its mark."))
        .Bind(topic => store
            .MarkResultAsync(topic, request.SearchedAt, request.Url, request.Mark, result => Markable(request.Url, result), cancellationToken)
            .Bind(marked => marked.ToResult<BatchResult, Error>(new ResultNotFoundError(topic, request.Url)))
            .Tap(_ => logger.ResultMarked(topic, request.Url, request.Mark?.ToString() ?? "none")));

    /// <summary>
    /// Only a verdict the Keeper gave can be agreed with: a result still waiting for one, discarded before the Keeper was
    /// asked, or discarded because it gave no answer says nothing about it.
    /// </summary>
    private static UnitResult<Error> Markable(string url, BatchResult result) => result.Verdict switch
    {
        { ByKeeper: true } => UnitResult.Success<Error>(),
        null => new ValidationError("ResultNotJudged", $"'{url}' has not been judged yet, so there is no verdict to mark."),
        { Failure: { } failure } => new ValidationError("ResultNotJudged", $"'{url}' has no verdict from the Keeper to mark: {failure}"),
        { Reason: var reason } => new ValidationError("ResultNotJudged", $"'{url}' was discarded before the Keeper saw it: {reason}")
    };
}
