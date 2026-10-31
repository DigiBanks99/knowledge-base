using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>The author's own brief and search queries for a topic.</summary>
/// <param name="Slug">The topic's slug, as the Topics page lists it.</param>
public record EditTopicCurationCommand(string Slug, string? Brief, IReadOnlyList<string>? Queries);

internal sealed class EditTopicCurationCommandHandler(
    ILogger<EditTopicCurationCommandHandler> logger,
    IResearchStore store,
    WikiPaths wikiPaths,
    TimeProvider timeProvider)
{
    /// <summary>
    /// The queries leave the machine whoever wrote them, so the author's are checked against the deny-list exactly as
    /// the Pleb's are. The edit is pinned: page changes no longer ask the Pleb to revisit it, and any curation already
    /// waiting is dropped so it cannot replace the edit. It answers any open question, and needs no approval.
    /// </summary>
    public async Task<Result<ResearchTopic, Error>> Handle(
        EditTopicCurationCommand request,
        CancellationToken cancellationToken) => await TopicSlugs
        .Resolve(request.Slug, wikiPaths)
        .Bind(async topic => await store
            .ReadDenyListAsync(cancellationToken)
            .Bind(denyList => TopicQueries.Validate(request.Brief, request.Queries, denyList.Matcher()))
            .Bind(edited => store
                .UpdateTopicAsync(
                    topic,
                    current => current.HasValue
                        ? Maybe.From(current.Value with
                        {
                            Brief = edited.Brief,
                            Queries = edited.Queries,
                            LastCuratedAt = timeProvider.GetUtcNow(),
                            ReviewQuestion = null,
                            ReviewRequestedAt = null,
                            ApprovedAt = null,
                            Pinned = true
                        })
                        : new TopicNotFoundError(topic),
                    cancellationToken)
                .Map(updated => updated.Value)))
        .Bind(async topic => await store.CancelCurationAsync(topic.Path, cancellationToken).Map(() => topic))
        .Tap(topic => logger.TopicReviewed(topic.Path, "edited"));
}
