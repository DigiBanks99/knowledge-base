namespace KnowledgeBase.Agent.Research.Models;

/// <summary>A result kept by a topic's search, with the query that found it.</summary>
/// <param name="Url">The result's normalised URL, under which the topic records it as seen.</param>
/// <param name="Page">The fetched page, or null while it waits to be fetched.</param>
/// <param name="Verdict">The Scout's verdict, or null while the result waits to be judged. A judged result is never judged again.</param>
public sealed record BatchResult(string Query, string Url, SearchResult Result, FetchedPage? Page = null, ScoutVerdict? Verdict = null);

/// <summary>
/// What the Scout decided about one result. A kept result carries the Scout's own summary and quotes (never the page's
/// text, which may hold instructions aimed at later agents); a discard carries its reason.
/// </summary>
/// <param name="Failure">Why there is no real verdict, when the result was discarded for lack of one.</param>
public sealed record ScoutVerdict(
    bool Kept,
    string? Summary,
    IReadOnlyList<string> Quotes,
    string? Reason,
    DateTimeOffset JudgedAt,
    string? Failure = null)
{
    public static ScoutVerdict Keep(string summary, IReadOnlyList<string> quotes, DateTimeOffset judgedAt) =>
        new(true, summary, quotes, null, judgedAt);

    public static ScoutVerdict Discard(string reason, DateTimeOffset judgedAt) =>
        new(false, null, [], reason, judgedAt);

    /// <summary>A discard because no verdict could be had, which says nothing about the result itself.</summary>
    public static ScoutVerdict Failed(string failure, DateTimeOffset judgedAt) =>
        new(false, null, [], null, judgedAt, failure);
}

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
/// <param name="SearchRunId">
/// The run recording the search that found these results, to which fetching and triage add what became of them. Null
/// for a batch written before searches were recorded.
/// </param>
public sealed record ResearchBatch(
    string Topic,
    DateTimeOffset SearchedAt,
    IReadOnlyList<BatchResult> Results,
    string? SearchRunId = null);

/// <summary>
/// A batch whose kept results were written to a research note, waiting for the ThreadWeaver to file it. The topic stays
/// in flight until its outcome is known.
/// </summary>
/// <param name="NoteFile">The name of the note in the notes folders, by which its run is found.</param>
public sealed record AwaitingBatch(ResearchBatch Batch, string NoteFile, DateTimeOffset NotedAt);
