using KnowledgeBase.Agent.Research.Fetch;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>The author's request to search a topic now rather than wait until it is due.</summary>
/// <param name="Slug">The topic's slug, as the Topics page lists it.</param>
public record ResearchTopicNowCommand(string Slug);

internal sealed class ResearchTopicNowCommandHandler(
    TopicSearch topicSearch,
    PendingResultFetch pendingResultFetch,
    WikiPaths wikiPaths)
{
    /// <summary>
    /// Searches the topic whether or not it is due, and outside the cycle's cap. A muted topic, one with no queries yet
    /// or one whose last results are still waiting is refused. The results' pages are then fetched in the background,
    /// as that takes minutes; the Topics page shows them as they arrive.
    /// </summary>
    public async Task<Result<TopicSearchOutcome, Error>> Handle(
        ResearchTopicNowCommand request,
        CancellationToken cancellationToken) => await TopicSlugs
        .Resolve(request.Slug, wikiPaths)
        .Bind(topic => topicSearch.SearchAsync(topic, cancellationToken))
        .Tap(searched =>
        {
            if (searched.Kept > 0)
            {
                pendingResultFetch.StartFetching(searched.Topic);
            }
        });
}
