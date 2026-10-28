using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research.Queries;

/// <summary>The results of a topic's last search that wait to be triaged.</summary>
/// <param name="Slug">The topic's slug, as the Topics page lists it.</param>
public record GetTopicResultsQuery(string Slug);

internal sealed class GetTopicResultsQueryHandler(IResearchStore store, WikiPaths wikiPaths)
{
    public async Task<Result<Maybe<ResearchBatch>, Error>> Handle(
        GetTopicResultsQuery request,
        CancellationToken cancellationToken) => await TopicSlugs
        .Resolve(request.Slug, wikiPaths)
        .Bind(topic => store.ReadPendingBatchAsync(topic, cancellationToken));
}
