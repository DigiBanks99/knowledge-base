namespace KnowledgeBase.Agent.Research.Models;

/// <summary>
/// The research state of one wiki topic. <see cref="Muted"/> is the author's decision from the UI: topic discovery and
/// curation never change it, and a topic pruned and later recreated starts fresh.
/// </summary>
/// <param name="Path">The topic's wiki path, such as <c>projects/foo</c> or <c>learning/bar.md</c>.</param>
/// <param name="Brief">A short, de-identified description of the topic for the web-facing agents, once curated.</param>
/// <param name="Queries">The search queries for the topic, once curated.</param>
/// <param name="LastCuratedAt">When the Pleb last set or kept the brief and queries; none until the first curation.</param>
public sealed record ResearchTopic(
    string Path,
    string? Brief,
    IReadOnlyList<string> Queries,
    bool Muted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastCuratedAt = null)
{
    public static ResearchTopic New(string path, DateTimeOffset createdAt) => new(path, null, [], false, createdAt);
}
