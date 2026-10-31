namespace KnowledgeBase.Agent.Research.Models;

/// <summary>
/// Asks the Pleb to revisit a topic's queries. One request is kept per topic: requesting again while one is waiting
/// merges into it, so a burst of changes to a topic becomes one curation.
/// </summary>
/// <param name="ChangedPaths">The wiki paths changed in the topic since it was last curated; empty on first discovery.</param>
/// <param name="RequestedAt">When the latest change was requested.</param>
/// <param name="FirstRequestedAt">
/// When the first change still waiting was requested, which places the request in the queue; none for a request that
/// has had nothing merged into it, whose place is <paramref name="RequestedAt"/>.
/// </param>
/// <param name="ByAuthor">
/// Whether the author asked for the curation from the Topics page (clarifying, rejecting or recurating), which puts
/// it ahead of curations that page changes asked for.
/// </param>
public sealed record CurationRequest(
    string Topic,
    IReadOnlyList<string> ChangedPaths,
    DateTimeOffset RequestedAt,
    DateTimeOffset? FirstRequestedAt = null,
    bool ByAuthor = false)
{
    /// <summary>
    /// How long the request has waited. Merging never moves it back in the queue, so a topic that keeps changing is
    /// still curated in its turn.
    /// </summary>
    public DateTimeOffset QueuedSince => FirstRequestedAt ?? RequestedAt;

    /// <summary>This request with a later one folded in: every changed path of both, requested at the later time.</summary>
    public CurationRequest Merge(CurationRequest later) => this with
    {
        ChangedPaths = [.. ChangedPaths.Union(later.ChangedPaths, StringComparer.Ordinal)],
        RequestedAt = later.RequestedAt > RequestedAt ? later.RequestedAt : RequestedAt,
        FirstRequestedAt = later.QueuedSince < QueuedSince ? later.QueuedSince : QueuedSince,
        ByAuthor = ByAuthor || later.ByAuthor
    };

    /// <summary>
    /// Whether this is the same request as <paramref name="other"/>, with nothing merged into either since. Record
    /// equality compares the changed paths by reference, so it cannot tell.
    /// </summary>
    public bool IsSameAs(CurationRequest other) =>
        Topic == other.Topic
        && RequestedAt == other.RequestedAt
        && ByAuthor == other.ByAuthor
        && ChangedPaths.SequenceEqual(other.ChangedPaths, StringComparer.Ordinal);
}
