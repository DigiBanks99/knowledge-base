using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>The author's request to have the Pleb curate a topic again, without a verdict on the current curation.</summary>
/// <param name="Slug">The topic's slug, as the Topics page lists it.</param>
public record RecurateTopicCommand(string Slug);

internal sealed class RecurateTopicCommandHandler(
    ILogger<RecurateTopicCommandHandler> logger,
    IResearchStore store,
    WikiPaths wikiPaths,
    TimeProvider timeProvider)
{
    /// <summary>Unpins a topic the author edited, so the Pleb may replace its brief and queries, and queues the curation.</summary>
    public async Task<Result<ResearchTopic, Error>> Handle(
        RecurateTopicCommand request,
        CancellationToken cancellationToken) => await TopicSlugs
        .Resolve(request.Slug, wikiPaths)
        .Bind(topic => store
            .UpdateTopicAsync(
                topic,
                current => current.HasValue
                    ? Maybe.From(current.Value with { Pinned = false })
                    : new TopicNotFoundError(topic),
                cancellationToken)
            .Map(updated => updated.Value))
        .Bind(topic => AuthorCuration.RequestAsync(store, topic, timeProvider, cancellationToken))
        .Tap(topic => logger.TopicReviewed(topic.Path, "recuration requested"));
}
