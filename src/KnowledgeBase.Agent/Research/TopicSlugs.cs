using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research;

/// <summary>Resolves a topic slug that arrived from the UI.</summary>
internal static class TopicSlugs
{
    /// <summary>The slug comes from the UI, so it must name a topic exactly and stay inside the wiki.</summary>
    public static Result<string, Error> Resolve(string slug, WikiPaths wikiPaths) => TopicPaths
        .FromSlug(slug)
        .Where(topic => wikiPaths.TryResolve(topic, out _))
        .ToResult<string, Error>(new ValidationError(
            "InvalidTopic",
            $"'{slug}' does not name a research topic. Topics are the top-level folders and pages of {string.Join(", ", TopicPaths.Sections)}."));
}
