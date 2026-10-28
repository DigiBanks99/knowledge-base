namespace KnowledgeBase.Agent.Research.Models;

/// <summary>Which topics a search cycle searches.</summary>
public static class DueTopics
{
    /// <summary>
    /// The topics due at <paramref name="now"/> (their last search plus interval has passed, or they were never
    /// searched), that are not muted, have queries to search and have no batch in flight: most overdue first, at most
    /// <paramref name="cap"/>.
    /// </summary>
    /// <param name="inFlight">The topics whose results are still waiting to be triaged or ingested.</param>
    public static IReadOnlyList<ResearchTopic> Select(
        IEnumerable<ResearchTopic> topics,
        IReadOnlySet<string> inFlight,
        DateTimeOffset now,
        int cap) =>
    [
        .. topics
            .Where(topic => !topic.Muted && topic.Queries.Count > 0 && !inFlight.Contains(topic.Path) && topic.DueAt <= now)
            .OrderBy(topic => topic.DueAt)
            .ThenBy(topic => topic.Path, StringComparer.Ordinal)
            .Take(Math.Max(0, cap))
    ];
}
