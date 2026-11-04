using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Queries;

/// <summary>The results of a topic's recent searches that the Keeper has judged, for the author to mark.</summary>
/// <param name="Slug">The topic's slug, as the Topics page lists it.</param>
public record ListJudgedResultsQuery(string Slug);

/// <summary>
/// One judged result, with the Keeper's probabilities beside the author's mark so the thresholds can be tuned from
/// them. Never the page itself.
/// </summary>
/// <param name="SearchedAt">When the search that found it ran, which names its batch when marking it.</param>
/// <param name="Reason">Why it was discarded, when it was.</param>
/// <param name="Failure">Why there is no real verdict.</param>
/// <param name="Markable">Whether the Keeper itself gave the verdict, which only then can be agreed with.</param>
public sealed record JudgedResult(
    DateTimeOffset SearchedAt,
    string Query,
    string Url,
    string Title,
    string Source,
    bool Kept,
    string? Reason,
    string? Failure,
    double? Useful,
    double? Injection,
    string? Kind,
    DateTimeOffset JudgedAt,
    ResultMark? Mark,
    bool Markable);

/// <summary>A topic's judged results, newest search first, and the thresholds the Keeper's probabilities are held to.</summary>
/// <param name="RetentionDays">How long a batch is kept once its outcome is known, after which its results are gone.</param>
/// <param name="AgreementWindowDays">How recently a result must have been judged for its mark to count on the Status page.</param>
public sealed record JudgedResults(
    IReadOnlyList<JudgedResult> Results,
    double KeepThreshold,
    double InjectionThreshold,
    int RetentionDays,
    int AgreementWindowDays);

internal sealed class ListJudgedResultsQueryHandler(
    IResearchStore store,
    WikiPaths wikiPaths,
    IOptions<ResearchSettings> settings)
{
    public async Task<Result<JudgedResults, Error>> Handle(
        ListJudgedResultsQuery request,
        CancellationToken cancellationToken) => await TopicSlugs
        .Resolve(request.Slug, wikiPaths)
        .Bind(topic => store.ListTopicBatchesAsync(topic, cancellationToken))
        .Map(batches => new JudgedResults(
            [
                .. batches.SelectMany(batch => batch.Results
                    .Where(result => result.Verdict is not null)
                    .Select(result => Judged(batch, result, result.Verdict!)))
            ],
            settings.Value.KeepThreshold,
            settings.Value.InjectionThreshold,
            ResolveAwaitingBatchesCommandHandler.CompletedBatchRetention.Days,
            KeeperAgreement.Window.Days));

    private static JudgedResult Judged(ResearchBatch batch, BatchResult result, Verdict verdict) => new(
        batch.SearchedAt,
        result.Query,
        result.Url,
        result.Result.Title,
        result.Result.Source,
        verdict.Kept,
        verdict.Reason,
        verdict.Failure,
        verdict.Useful,
        verdict.Injection,
        verdict.Kind,
        verdict.JudgedAt,
        result.Mark,
        verdict.ByKeeper);
}
