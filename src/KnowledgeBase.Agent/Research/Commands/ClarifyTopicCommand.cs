using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>The author's answer to the question the Pleb asked about a topic.</summary>
/// <param name="Slug">The topic's slug, as the Topics page lists it.</param>
public record ClarifyTopicCommand(string Slug, string? Answer);

internal sealed class ClarifyTopicCommandHandler(
    ILogger<ClarifyTopicCommandHandler> logger,
    IOptions<ResearchSettings> settings,
    IResearchStore store,
    WikiPaths wikiPaths,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Keeps the answer, with its question, as guidance for every later curation, and asks the Pleb to curate the topic
    /// again. The question stays open, so the topic is still not searched, until the Pleb sets new queries.
    /// </summary>
    public async Task<Result<ResearchTopic, Error>> Handle(
        ClarifyTopicCommand request,
        CancellationToken cancellationToken) => await AuthorText
        .Validate(request.Answer, "answer", required: true)
        .Bind(answer => TopicSlugs
            .Resolve(request.Slug, wikiPaths)
            .Bind(topic => store
                .UpdateTopicAsync(
                    topic,
                    current => !current.HasValue ? new TopicNotFoundError(topic)
                        : !current.Value.NeedsReview ? new TopicNotInReviewError(topic)
                        : Maybe.From((current.Value with { Pinned = false }).WithGuidance(
                            new AuthorGuidance(GuidanceKind.Clarification, answer, timeProvider.GetUtcNow(), current.Value.ReviewQuestion),
                            settings.Value.GuidanceLimit)),
                    cancellationToken)
                .Map(updated => updated.Value)))
        .Bind(topic => AuthorCuration.RequestAsync(store, topic, timeProvider, cancellationToken))
        .Tap(topic => logger.TopicReviewed(topic.Path, "clarified"));
}
