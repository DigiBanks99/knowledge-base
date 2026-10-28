namespace KnowledgeBase.Agent.Research.Models;

/// <summary>
/// The research state of one wiki topic. <see cref="Muted"/> is the author's decision from the UI: topic discovery and
/// curation never change it, and a topic pruned and later recreated starts fresh.
/// </summary>
/// <param name="Path">The topic's wiki path, such as <c>projects/foo</c> or <c>learning/bar.md</c>.</param>
/// <param name="Brief">A short, de-identified description of the topic for the web-facing agents, once curated.</param>
/// <param name="Queries">The search queries for the topic, once curated.</param>
/// <param name="LastCuratedAt">When the Pleb last set or kept the brief and queries; none until the first curation.</param>
/// <param name="LastSearchedAt">When the topic's queries were last searched; none until the first search.</param>
/// <param name="Interval">How long after a search the topic is due again; none for <see cref="DefaultInterval"/>.</param>
/// <param name="SeenUrls">
/// Every normalised result URL the topic's searches returned, with when it was first seen, so a result is only ever
/// considered once; none until the first search.
/// </param>
public sealed record ResearchTopic(
    string Path,
    string? Brief,
    IReadOnlyList<string> Queries,
    bool Muted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastCuratedAt = null,
    DateTimeOffset? LastSearchedAt = null,
    TimeSpan? Interval = null,
    IReadOnlyDictionary<string, DateTimeOffset>? SeenUrls = null)
{
    /// <summary>How long a topic waits between searches until its outcomes say otherwise.</summary>
    public static TimeSpan DefaultInterval { get; } = TimeSpan.FromHours(24);

    public static ResearchTopic New(string path, DateTimeOffset createdAt) => new(path, null, [], false, createdAt);

    /// <summary>When the topic is next due to be searched; a topic never searched is due from its creation.</summary>
    public DateTimeOffset DueAt => LastSearchedAt.HasValue ? LastSearchedAt.Value + (Interval ?? DefaultInterval) : CreatedAt;

    /// <summary>Whether the topic has been searched for <paramref name="normalisedUrl"/> before.</summary>
    public bool HasSeen(string normalisedUrl) => SeenUrls?.ContainsKey(normalisedUrl) == true;

    /// <summary>This topic searched at <paramref name="searchedAt"/>, having seen <paramref name="urls"/> too.</summary>
    public ResearchTopic Searched(DateTimeOffset searchedAt, IEnumerable<string> urls) =>
        Saw(searchedAt, urls) with { LastSearchedAt = searchedAt };

    /// <summary>
    /// This topic having seen <paramref name="urls"/> at <paramref name="seenAt"/>, but not counted as searched: for a
    /// search some sources failed, whose results from since the last search must still be looked for next time.
    /// </summary>
    public ResearchTopic Saw(DateTimeOffset seenAt, IEnumerable<string> urls)
    {
        Dictionary<string, DateTimeOffset> seen = new(SeenUrls ?? new Dictionary<string, DateTimeOffset>(), StringComparer.Ordinal);
        foreach (string url in urls)
        {
            seen.TryAdd(url, seenAt);
        }

        return this with { SeenUrls = seen };
    }
}
