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
            .Map(IReadOnlyList<TopicSummary> (requests) =>
            {
                HashSet<string> awaitingCuration = [.. requests.Select(request => request.Topic)];
                return
                [
                    .. topics
                        .OrderBy(topic => topic.Path, StringComparer.OrdinalIgnoreCase)
                        .Select(topic => Summarise(topic, awaitingCuration.Contains(topic.Path)))
                ];
            }));

    private static TopicSummary Summarise(ResearchTopic topic, bool awaitingCuration) => new(
        topic.Path,
        TopicPaths.Slug(topic.Path),
        topic.Muted ? TopicState.Muted : awaitingCuration ? TopicState.AwaitingCuration : TopicState.Idle,
        topic.Brief,
        topic.Queries,
        topic.Muted);
}
