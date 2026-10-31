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
/// <param name="LastPositiveAt">When the ThreadWeaver last filed something from the topic's research; none until it has.</param>
/// <param name="ConsecutiveFailures">How many research notes in a row ended in the failed notes folder.</param>
/// <param name="ReviewQuestion">
/// What the Pleb could not settle about the topic and asked the author; while it is open the topic is not searched.
/// </param>
/// <param name="Guidance">The author's latest clarifications and rejections, given to every later curation.</param>
/// <param name="Pinned">
/// Whether the author edited the brief and queries, so page changes no longer ask the Pleb to revisit them.
/// </param>
/// <param name="ApprovedAt">When the author approved the current brief and queries; none once they change.</param>
/// <param name="ReviewRequestedAt">When the Pleb asked its open question, so an older answer to it does not count.</param>
public sealed record ResearchTopic(
    string Path,
    string? Brief,
    IReadOnlyList<string> Queries,
    bool Muted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastCuratedAt = null,
    DateTimeOffset? LastSearchedAt = null,
    TimeSpan? Interval = null,
    IReadOnlyDictionary<string, DateTimeOffset>? SeenUrls = null,
    DateTimeOffset? LastPositiveAt = null,
    int ConsecutiveFailures = 0,
    string? ReviewQuestion = null,
    IReadOnlyList<AuthorGuidance>? Guidance = null,
    bool Pinned = false,
    DateTimeOffset? ApprovedAt = null,
    DateTimeOffset? ReviewRequestedAt = null)
{
    /// <summary>How long a topic waits between searches until its outcomes say otherwise.</summary>
    public static TimeSpan DefaultInterval { get; } = TimeSpan.FromHours(24);

    /// <summary>The longest a topic backs off to when its research keeps coming to nothing.</summary>
    public static TimeSpan MaxInterval { get; } = TimeSpan.FromDays(30);

    /// <summary>How long a seen URL is remembered; after that a result may be considered again.</summary>
    public static TimeSpan SeenUrlRetention { get; } = TimeSpan.FromDays(180);

    /// <summary>How many failed notes in a row flag a topic as failing.</summary>
    public const int FailureLimit = 3;

    public static ResearchTopic New(string path, DateTimeOffset createdAt) => new(path, null, [], false, createdAt);

    /// <summary>When the topic is next due to be searched; a topic never searched is due from its creation.</summary>
    public DateTimeOffset DueAt => LastSearchedAt.HasValue ? LastSearchedAt.Value + (Interval ?? DefaultInterval) : CreatedAt;

    /// <summary>Whether the pipeline keeps failing on the topic's research, which is not the same as the topic being quiet.</summary>
    public bool IsFailing => ConsecutiveFailures >= FailureLimit;

    /// <summary>Whether the Pleb asked the author a question about the topic that is still unanswered by a new curation.</summary>
    public bool NeedsReview => ReviewQuestion is not null;

    /// <summary>Whether the author has answered the open question since the Pleb asked it.</summary>
    public bool ReviewAnswered => NeedsReview && (Guidance ?? []).Any(entry =>
        entry.Kind == GuidanceKind.Clarification && (ReviewRequestedAt is null || entry.At >= ReviewRequestedAt));

    /// <summary>Whether the author rejected the brief and queries the topic has now, so the Pleb may not keep them.</summary>
    public bool CurationRejected => IsRejected(Queries);

    /// <summary>Whether <paramref name="queries"/> are a set of queries the author has rejected for this topic.</summary>
    public bool IsRejected(IReadOnlyList<string> queries) => queries.Count > 0 && (Guidance ?? []).Any(entry =>
        entry.Kind == GuidanceKind.Rejection
        && entry.RejectedQueries is { } rejected
        && rejected.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(queries.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// This topic with <paramref name="entry"/> added to its guidance, keeping only the latest <paramref name="limit"/>
    /// entries so the curation prompt stays within its budget.
    /// </summary>
    public ResearchTopic WithGuidance(AuthorGuidance entry, int limit) =>
        this with { Guidance = [.. (Guidance ?? []).Append(entry).TakeLast(Math.Max(1, limit))] };

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
        // Expired URLs are dropped as new ones are added, so the record never grows without bound.
        Dictionary<string, DateTimeOffset> seen = new(WithoutExpiredUrls(seenAt).SeenUrls ?? new Dictionary<string, DateTimeOffset>(), StringComparer.Ordinal);
        foreach (string url in urls)
        {
            seen.TryAdd(url, seenAt);
        }

        return this with { SeenUrls = seen };
    }

    /// <summary>The ThreadWeaver filed something from the topic's research: check it again after the default interval.</summary>
    public ResearchTopic Positive(DateTimeOffset at) =>
        this with { Interval = DefaultInterval, LastPositiveAt = at, ConsecutiveFailures = 0 };

    /// <summary>
    /// Nothing came of the topic's research (all discarded, or nothing filed): wait twice as long, up to
    /// <see cref="MaxInterval"/>. A quiet topic is not a failing one, so failures reset.
    /// </summary>
    public ResearchTopic Empty() =>
        this with { Interval = Min(2 * (Interval ?? DefaultInterval), MaxInterval), ConsecutiveFailures = 0 };

    /// <summary>The research note failed: the interval says nothing about the topic, so it stays.</summary>
    public ResearchTopic Failed() => this with { ConsecutiveFailures = ConsecutiveFailures + 1 };

    /// <summary>This topic having forgotten the URLs first seen more than <see cref="SeenUrlRetention"/> before <paramref name="now"/>.</summary>
    public ResearchTopic WithoutExpiredUrls(DateTimeOffset now)
    {
        if (SeenUrls is null || !SeenUrls.Values.Any(seenAt => now - seenAt > SeenUrlRetention))
        {
            return this;
        }

        return this with
        {
            SeenUrls = SeenUrls
                .Where(seen => now - seen.Value <= SeenUrlRetention)
                .ToDictionary(seen => seen.Key, seen => seen.Value, StringComparer.Ordinal)
        };
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second) => first < second ? first : second;
}
