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
                .Map(IReadOnlyList<TopicSummary> (failed) =>
                {
                    HashSet<string> awaitingCuration = [.. requests.Select(request => request.Topic)];
                    HashSet<string> curationFailed = [.. failed.Select(failure => failure.Request.Topic)];
                    return
                    [
                        .. topics
                            .OrderBy(topic => topic.Path, StringComparer.OrdinalIgnoreCase)
                            .Select(topic => Summarise(topic, State(topic, awaitingCuration, curationFailed)))
                    ];
                })));

    /// <summary>A failed curation is superseded by a newer request waiting for the same topic.</summary>
    private static TopicState State(ResearchTopic topic, HashSet<string> awaitingCuration, HashSet<string> curationFailed) =>
        topic.Muted ? TopicState.Muted
        : awaitingCuration.Contains(topic.Path) ? TopicState.AwaitingCuration
        : curationFailed.Contains(topic.Path) ? TopicState.CurationFailed
        : TopicState.Idle;

    private static TopicSummary Summarise(ResearchTopic topic, TopicState state) => new(
        topic.Path,
        TopicPaths.Slug(topic.Path),
        state,
        topic.Brief,
        topic.Queries,
        topic.Muted,
        topic.LastCuratedAt);
}
