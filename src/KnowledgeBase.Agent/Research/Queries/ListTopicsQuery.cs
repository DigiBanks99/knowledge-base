using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Queries;

public record struct ListTopicsQuery;

internal sealed class ListTopicsQueryHandler(IResearchStore store)
{
    public async Task<Result<IReadOnlyList<TopicSummary>, Error>> Handle(
        ListTopicsQuery _,
        CancellationToken cancellationToken) => await store
        .ListTopicsAsync(cancellationToken)
        .Bind(async topics => await store
            .ListCurationRequestsAsync(cancellationToken)
            .Bind(async requests => await store
                .ListFailedCurationsAsync(cancellationToken)
                .Bind(async failed => await store
                    .ListPendingBatchesAsync(cancellationToken)
                    .Map(IReadOnlyList<TopicSummary> (batches) =>
                    {
                        HashSet<string> awaitingCuration = [.. requests.Select(request => request.Topic)];
                        HashSet<string> curationFailed = [.. failed.Select(failure => failure.Request.Topic)];
                        // A batch missing its topic or results (edited by hand, say) is left out rather than failing the page.
                        Dictionary<string, int> pendingResults = batches
                            .Where(batch => batch.Topic is not null && batch.Results is not null)
                            .DistinctBy(batch => batch.Topic, StringComparer.Ordinal)
                            .ToDictionary(batch => batch.Topic, batch => batch.Results.Count, StringComparer.Ordinal);
                        return
                        [
                            .. topics
                                .OrderBy(topic => topic.Path, StringComparer.OrdinalIgnoreCase)
                                .Select(topic => Summarise(
                                    topic,
                                    State(topic, awaitingCuration, curationFailed, pendingResults),
                                    pendingResults.GetValueOrDefault(topic.Path)))
                        ];
                    }))));

    /// <summary>
    /// Results waiting for triage come first, as they block the topic's next search. A failed curation is superseded by
    /// a newer request waiting for the same topic.
    /// </summary>
    private static TopicState State(
        ResearchTopic topic,
        HashSet<string> awaitingCuration,
        HashSet<string> curationFailed,
        Dictionary<string, int> pendingResults) =>
        topic.Muted ? TopicState.Muted
        : pendingResults.ContainsKey(topic.Path) ? TopicState.Triaging
        : awaitingCuration.Contains(topic.Path) ? TopicState.AwaitingCuration
        : curationFailed.Contains(topic.Path) ? TopicState.CurationFailed
        : topic.IsFailing ? TopicState.Failing
        : TopicState.Idle;

    private static TopicSummary Summarise(ResearchTopic topic, TopicState state, int pendingResults) => new(
        topic.Path,
        TopicPaths.Slug(topic.Path),
        state,
        topic.Brief,
        topic.Queries,
        topic.Muted,
        topic.LastCuratedAt,
        topic.LastSearchedAt,
        topic.Queries.Count > 0 ? topic.DueAt : null,
        pendingResults,
        topic.Interval ?? ResearchTopic.DefaultInterval,
        topic.LastPositiveAt);
}
