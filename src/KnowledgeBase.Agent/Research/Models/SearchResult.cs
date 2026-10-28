namespace KnowledgeBase.Agent.Research.Models;

/// <summary>One result a search source returned for a query.</summary>
/// <param name="Source">The name of the search source, such as <c>SearXNG</c>.</param>
/// <param name="PublishedAt">When the result was published, when the source says.</param>
public sealed record SearchResult(
    string Url,
    string Title,
    string? Snippet,
    string Source,
    DateTimeOffset? PublishedAt);
