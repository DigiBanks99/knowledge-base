using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>The author's rejection of a topic's brief and queries, with an optional reason.</summary>
/// <param name="Slug">The topic's slug, as the Topics page lists it.</param>
public record RejectTopicCurationCommand(string Slug, string? Reason);

internal sealed class RejectTopicCurationCommandHandler(
    ILogger<RejectTopicCurationCommandHandler> logger,
    IOptions<ResearchSettings> settings,
    IResearchStore store,
    WikiPaths wikiPaths,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Keeps the rejected brief and queries with the reason as guidance for every later curation, so the Pleb does not
    /// propose them again, and asks the Pleb to curate the topic again. The topic is searched with the rejected queries
    /// until then: they are the best it has, and the author can mute it instead.
    /// </summary>
    public async Task<Result<ResearchTopic, Error>> Handle(
        RejectTopicCurationCommand request,
        CancellationToken cancellationToken) => await AuthorText
        .Validate(request.Reason, "reason", required: false)
        .Bind(reason => TopicSlugs
            .Resolve(request.Slug, wikiPaths)
            .Bind(topic => store
                .UpdateTopicAsync(
                    topic,
                    current => !current.HasValue ? new TopicNotFoundError(topic)
                        : current.Value.Queries.Count == 0 ? new TopicNotCuratedError(topic)
                        : Maybe.From((current.Value with { Pinned = false, ApprovedAt = null }).WithGuidance(
                            new AuthorGuidance(
                                GuidanceKind.Rejection,
                                reason,
                                timeProvider.GetUtcNow(),
                                RejectedBrief: current.Value.Brief,
                                RejectedQueries: current.Value.Queries),
                            settings.Value.GuidanceLimit)),
                    cancellationToken)
                .Map(updated => updated.Value)))
        .Bind(topic => AuthorCuration.RequestAsync(store, topic, timeProvider, cancellationToken))
        .Tap(topic => logger.TopicReviewed(topic.Path, "rejected"));
}
