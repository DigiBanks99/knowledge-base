namespace KnowledgeBase.Agent.Research.Models;

/// <summary>
/// Asks the Pleb to revisit a topic's queries. One request is kept per topic: requesting again while one is waiting
/// merges into it, so a burst of changes to a topic becomes one curation.
/// </summary>
/// <param name="ChangedPaths">The wiki paths changed in the topic since it was last curated; empty on first discovery.</param>
public sealed record CurationRequest(string Topic, IReadOnlyList<string> ChangedPaths, DateTimeOffset RequestedAt)
{
    /// <summary>This request with a later one folded in: every changed path of both, requested at the later time.</summary>
    public CurationRequest Merge(CurationRequest later) => this with
    {
        ChangedPaths = [.. ChangedPaths.Union(later.ChangedPaths, StringComparer.Ordinal)],
        RequestedAt = later.RequestedAt > RequestedAt ? later.RequestedAt : RequestedAt
    };
}
