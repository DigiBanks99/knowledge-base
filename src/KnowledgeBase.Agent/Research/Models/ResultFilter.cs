namespace KnowledgeBase.Agent.Research.Models;

/// <summary>A search's results after filtering, and the URLs the topic now records as seen.</summary>
/// <param name="Blocked">Results dropped for a blocked domain or a URL that could not be used.</param>
/// <param name="AlreadySeen">Results the topic had seen, or that an earlier result of the same search found.</param>
/// <param name="TooOld">Results dated before the cutoff.</param>
public sealed record FilteredResults(
    IReadOnlyList<BatchResult> Kept,
    IReadOnlyList<string> Seen,
    int Blocked,
    int AlreadySeen,
    int TooOld);

/// <summary>Decides which of a topic's search results are worth triaging.</summary>
public static class ResultFilter
{
    /// <summary>How far back a topic's first search looks for dated results.</summary>
    public static TimeSpan FirstSearchWindow { get; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Applies the filters in order: a blocked domain, a URL the topic has seen (or that an earlier result of this
    /// search already found), then recency. A dated result must be newer than the topic's last search, or than
    /// <see cref="FirstSearchWindow"/> on its first; an undated result is kept, as being unseen is all that can be
    /// known of it. Every result that is not blocked is recorded as seen, kept or not, so it is never considered again.
    /// </summary>
    public static FilteredResults Apply(
        IEnumerable<(string Query, SearchResult Result)> results,
        ResearchTopic topic,
        BlockedDomains blockedDomains,
        DateTimeOffset now)
    {
        DateTimeOffset cutoff = topic.LastSearchedAt ?? now - FirstSearchWindow;
        List<BatchResult> kept = [];
        List<string> seen = [];
        HashSet<string> seenThisSearch = new(StringComparer.Ordinal);
        int blocked = 0;
        int alreadySeen = 0;
        int tooOld = 0;
        foreach ((string query, SearchResult result) in results)
        {
            Maybe<string> url = ResultUrls.Normalise(result.Url);
            if (url.HasNoValue || blockedDomains.Blocks(result.Url))
            {
                blocked++;
                continue;
            }

            if (topic.HasSeen(url.Value) || !seenThisSearch.Add(url.Value))
            {
                alreadySeen++;
                continue;
            }

            seen.Add(url.Value);
            if (result.PublishedAt is null || result.PublishedAt > cutoff)
            {
                kept.Add(new BatchResult(query, url.Value, result));
            }
            else
            {
                tooOld++;
            }
        }

        return new FilteredResults(kept, seen, blocked, alreadySeen, tooOld);
    }
}
