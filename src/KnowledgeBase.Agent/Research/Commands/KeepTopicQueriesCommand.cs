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
    /// can keep them; a topic never curated must be given some. A topic whose question the author is answering must
    /// be given new ones too, as its question says the current ones are not good enough.
    /// </summary>
    public async Task<Result<ResearchTopic, Error>> Handle(
        KeepTopicQueriesCommand request,
        CancellationToken cancellationToken) => await store
        .UpdateTopicAsync(
            request.Topic,
            current => !current.HasValue ? new TopicNotFoundError(request.Topic)
                : current.Value.NeedsReview ? new ValidationError(
                    "ReviewPending",
                    $"Topic '{request.Topic}' is waiting on the author's answer to your earlier question, so its queries cannot be kept. Call `SetTopicQueries` with a brief and queries that follow the author's guidance in the prompt, or `RequestHumanReview` if the subject is still unclear.")
                : current.Value.CurationRejected ? new ValidationError(
                    "RejectedQueries",
                    $"The author rejected topic '{request.Topic}''s current queries, so they cannot be kept. Call `SetTopicQueries` with a new brief and queries that follow the author's guidance in the prompt.")
                : current.Value.Queries.Count == 0 ? new ValidationError(
                    "NoQueriesToKeep",
                    $"Topic '{request.Topic}' has no queries yet, so there is nothing to keep. Call `SetTopicQueries` with a brief and up to {TopicQueries.MaxQueries} queries instead.")
                : Maybe.From(current.Value with { LastCuratedAt = timeProvider.GetUtcNow() }),
            cancellationToken)
        .Map(updated => updated.Value)
        .Tap(topic => logger.TopicQueriesKept(topic.Path));
}
