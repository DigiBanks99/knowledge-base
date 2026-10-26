using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>The author's decision to stop (or resume) researching a topic, without moving it in the wiki.</summary>
/// <param name="Slug">The topic's slug, as the Topics page lists it.</param>
public record SetTopicMutedCommand(string Slug, bool Muted);

internal sealed class SetTopicMutedCommandHandler(
    ILogger<SetTopicMutedCommandHandler> logger,
    IResearchStore store,
    WikiPaths wikiPaths)
{
    public async Task<Result<ResearchTopic, Error>> Handle(
        SetTopicMutedCommand request,
        CancellationToken cancellationToken) => await TopicOf(request.Slug)
        .Bind(topic => store
            .UpdateTopicAsync(
                topic,
                current => current.HasValue
                    ? Maybe.From(current.Value with { Muted = request.Muted })
                    : new TopicNotFoundError(topic),
                cancellationToken)
            .Map(updated => updated.Value))
        .Tap(topic => logger.TopicMuteSet(topic.Path, topic.Muted));

    /// <summary>The slug comes from the UI, so it must name a topic exactly and stay inside the wiki.</summary>
    private Result<string, Error> TopicOf(string slug) => TopicPaths
        .FromSlug(slug)
        .Where(topic => wikiPaths.TryResolve(topic, out _))
        .ToResult<string, Error>(new ValidationError(
            "InvalidTopic",
            $"'{slug}' does not name a research topic. Topics are the top-level folders and pages of {string.Join(", ", TopicPaths.Sections)}."));
}
