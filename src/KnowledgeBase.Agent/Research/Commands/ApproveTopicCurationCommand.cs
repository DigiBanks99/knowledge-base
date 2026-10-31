using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>The author's approval of a topic's brief and queries.</summary>
/// <param name="Slug">The topic's slug, as the Topics page lists it.</param>
public record ApproveTopicCurationCommand(string Slug);

internal sealed class ApproveTopicCurationCommandHandler(
    ILogger<ApproveTopicCurationCommandHandler> logger,
    IResearchStore store,
    WikiPaths wikiPaths,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Records the approval on the topic, until its brief and queries next change. A topic with no queries or an open
    /// question has nothing to approve yet.
    /// </summary>
    public async Task<Result<ResearchTopic, Error>> Handle(
        ApproveTopicCurationCommand request,
        CancellationToken cancellationToken) => await TopicSlugs
        .Resolve(request.Slug, wikiPaths)
        .Bind(topic => store
            .UpdateTopicAsync(
                topic,
                current => !current.HasValue ? new TopicNotFoundError(topic)
                    : current.Value.NeedsReview ? new TopicNeedsReviewError(topic)
                    : current.Value.Queries.Count == 0 ? new TopicNotCuratedError(topic)
                    : Maybe.From(current.Value with { ApprovedAt = timeProvider.GetUtcNow() }),
                cancellationToken)
            .Map(updated => updated.Value))
        .Tap(topic => logger.TopicReviewed(topic.Path, "approved"));
}
