using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>The Pleb's new brief and search queries for a topic.</summary>
/// <param name="Topic">The topic's wiki path, such as <c>projects/foo</c>.</param>
public record SetTopicQueriesCommand(string Topic, string? Brief, IReadOnlyList<string>? Queries);

internal sealed class SetTopicQueriesCommandHandler(
    ILogger<SetTopicQueriesCommandHandler> logger,
    IResearchStore store,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Checks the brief and queries against the deny-list as it is now, then stores them as the topic's curation.
    /// Nothing is stored when they are refused.
    /// </summary>
    public async Task<Result<ResearchTopic, Error>> Handle(
        SetTopicQueriesCommand request,
        CancellationToken cancellationToken) => await store
        .ReadDenyListAsync(cancellationToken)
        .Bind(denyList => TopicQueries.Validate(request.Brief, request.Queries, denyList.Matcher()))
        .Bind(curated => store
            .UpdateTopicAsync(
                request.Topic,
                current => current.HasValue
                    ? Maybe.From(current.Value with
                    {
                        Brief = curated.Brief,
                        Queries = curated.Queries,
                        LastCuratedAt = timeProvider.GetUtcNow()
                    })
                    : new TopicNotFoundError(request.Topic),
                cancellationToken)
            .Map(updated => updated.Value))
        .Tap(topic => logger.TopicQueriesSet(topic.Path, topic.Queries.Count));
}
