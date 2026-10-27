using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>The Pleb's decision that nothing relevant changed, so a topic keeps its brief and queries.</summary>
/// <param name="Topic">The topic's wiki path, such as <c>projects/foo</c>.</param>
public record KeepTopicQueriesCommand(string Topic);

internal sealed class KeepTopicQueriesCommandHandler(
    ILogger<KeepTopicQueriesCommandHandler> logger,
    IResearchStore store,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Keeping the same queries keeps the staleness signal comparable between cycles. Only a topic that has queries
    /// can keep them; a topic never curated must be given some.
    /// </summary>
    public async Task<Result<ResearchTopic, Error>> Handle(
        KeepTopicQueriesCommand request,
        CancellationToken cancellationToken) => await store
        .UpdateTopicAsync(
            request.Topic,
            current => current.HasValue
                ? current.Value.Queries.Count > 0
                    ? Maybe.From(current.Value with { LastCuratedAt = timeProvider.GetUtcNow() })
                    : new ValidationError(
                        "NoQueriesToKeep",
                        $"Topic '{request.Topic}' has no queries yet, so there is nothing to keep. Call `SetTopicQueries` with a brief and up to {TopicQueries.MaxQueries} queries instead.")
                : new TopicNotFoundError(request.Topic),
            cancellationToken)
        .Map(updated => updated.Value)
        .Tap(topic => logger.TopicQueriesKept(topic.Path));
}
