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
    /// Nothing is stored when they are refused. New queries answer any question the Pleb asked and need approving
    /// again. A topic the author edited meanwhile keeps the author's brief and queries.
    /// </summary>
    public async Task<Result<ResearchTopic, Error>> Handle(
        SetTopicQueriesCommand request,
        CancellationToken cancellationToken) => await store
        .ReadDenyListAsync(cancellationToken)
        .Bind(denyList => TopicQueries.Validate(request.Brief, request.Queries, denyList.Matcher()))
        .Bind(curated => store
            .UpdateTopicAsync(
                request.Topic,
                current => !current.HasValue ? new TopicNotFoundError(request.Topic)
                    : current.Value.Pinned ? new TopicPinnedError(request.Topic)
                    : current.Value.IsRejected(curated.Queries) ? new ValidationError(
                        "RejectedQueries",
                        "The author rejected exactly these queries for this topic. Write different queries that follow the author's guidance in the prompt.")
                    : Maybe.From(current.Value with
                    {
                        Brief = curated.Brief,
                        Queries = curated.Queries,
                        LastCuratedAt = timeProvider.GetUtcNow(),
                        ReviewQuestion = null,
                        ReviewRequestedAt = null,
                        ApprovedAt = null
                    }),
                cancellationToken)
            .Map(updated => updated.Value))
        .Tap(topic => logger.TopicQueriesSet(topic.Path, topic.Queries.Count));
}
