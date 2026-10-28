namespace KnowledgeBase.Agent.Research.Models;

/// <summary>A result kept by a topic's search, with the query that found it.</summary>
/// <param name="Url">The result's normalised URL, under which the topic records it as seen.</param>
/// <param name="Page">The fetched page, or null while it waits to be fetched.</param>
public sealed record BatchResult(string Query, string Url, SearchResult Result, FetchedPage? Page = null);

/// <summary>
/// What fetching a result's page gave: its main content as markdown, or why there is none. A fetch that failed is
/// recorded rather than retried, so one bad page never holds up its batch.
/// </summary>
/// <param name="Truncated">Whether the content was cut to fit the triage token budget.</param>
/// <param name="Error">Why the page could not be fetched; null when <paramref name="Markdown"/> holds its content.</param>
public sealed record FetchedPage(DateTimeOffset FetchedAt, string? Markdown, bool Truncated, string? Error)
{
    public static FetchedPage Content(DateTimeOffset fetchedAt, string markdown, bool truncated) =>
        new(fetchedAt, markdown, truncated, null);

    public static FetchedPage Failed(DateTimeOffset fetchedAt, string error) => new(fetchedAt, null, false, error);
}

/// <summary>
/// The results one search of a topic kept, waiting for the Scout. While it waits the topic is in flight and is not
/// searched again.
/// </summary>
public sealed record ResearchBatch(string Topic, DateTimeOffset SearchedAt, IReadOnlyList<BatchResult> Results);
