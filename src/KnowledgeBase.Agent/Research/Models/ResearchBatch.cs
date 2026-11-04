using System.Text.Json.Serialization;

namespace KnowledgeBase.Agent.Research.Models;

/// <summary>A result kept by a topic's search, with the query that found it.</summary>
/// <param name="Url">The result's normalised URL, under which the topic records it as seen.</param>
/// <param name="Page">The fetched page, or null while it waits to be fetched.</param>
/// <param name="Verdict">The result's verdict, or null while it waits to be judged. A judged result is never judged again.</param>
/// <param name="Attempts">
/// How many times the Keeper failed to answer for the result. It is given up on after a few, so a page the model can
/// never answer doesn't hold up its batch.
/// </param>
/// <param name="SummaryAttempts">
/// How many Clerk runs for a kept result timed out. It is given up on after a few, keeping its quotes alone.
/// </param>
/// <param name="Mark">
/// The author's own judgement of a judged result, which only feeds the Keeper's agreement figures: it never changes the
/// verdict or what becomes of the result.
/// </param>
public sealed record BatchResult(
    string Query,
    string Url,
    SearchResult Result,
    FetchedPage? Page = null,
    Verdict? Verdict = null,
    int Attempts = 0,
    int SummaryAttempts = 0,
    ResultMark? Mark = null)
{
    /// <summary>
    /// Kept, and waiting for the Clerk: it has neither a summary nor a failed Clerk run. A discarded result, including
    /// one flagged as an injection, never waits for one, so its page never reaches a generative model.
    /// </summary>
    public bool NeedsSummary => Verdict is { Kept: true, Summary: null, SummaryFailure: null };

    /// <summary>Whether triage is done with the result: it is judged and, if kept, summarised or given up on.</summary>
    public bool Triaged => Verdict is not null && !NeedsSummary;
}

/// <summary>
/// What triage decided about one result. A kept result carries exact quotes and, once written, a summary (never the
/// page's whole text, which may hold instructions aimed at later agents); a discard carries its reason. The Keeper's
/// probabilities are stored with the verdict so the thresholds can be tuned against real results.
/// </summary>
/// <param name="Summary">The Clerk's summary of a kept result; null until one is written.</param>
/// <param name="Failure">Why there is no real verdict, when the result was discarded for lack of one.</param>
/// <param name="SummaryFailure">Why a kept result has no summary, once the Clerk's run for it failed.</param>
/// <param name="Useful">The Keeper's probability that the result is worth passing on.</param>
/// <param name="Injection">The Keeper's probability that the result holds instructions aimed at an AI.</param>
/// <param name="Kind">What kind of content a kept result is. Unreliable, so stored but never gated on.</param>
/// <param name="KindProbability">The Keeper's probability for <paramref name="Kind"/>.</param>
public sealed record Verdict(
    bool Kept,
    string? Summary,
    IReadOnlyList<string> Quotes,
    string? Reason,
    DateTimeOffset JudgedAt,
    string? Failure = null,
    double? Useful = null,
    double? Injection = null,
    string? Kind = null,
    double? KindProbability = null,
    string? SummaryFailure = null)
{
    /// <summary>
    /// Whether the Keeper itself judged the result, so the author can agree or disagree with it. A discard made before
    /// the Keeper was asked (an unfetchable page, a page with no prose) or for lack of an answer has no probabilities.
    /// </summary>
    [JsonIgnore]
    public bool ByKeeper => Failure is null && Useful is not null;

    public static Verdict Keep(string? summary, IReadOnlyList<string> quotes, DateTimeOffset judgedAt) =>
        new(true, summary, quotes, null, judgedAt);

    public static Verdict Discard(string reason, DateTimeOffset judgedAt) =>
        new(false, null, [], reason, judgedAt);

    /// <summary>A discard because no verdict could be had, which says nothing about the result itself.</summary>
    public static Verdict Failed(string failure, DateTimeOffset judgedAt) =>
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
/// The results one search of a topic kept, waiting for triage. While it waits the topic is in flight and is not
/// searched again.
/// </summary>
/// <param name="SearchRunId">
/// The run recording the search that found these results, to which fetching and triage add what became of them. Null
/// for a batch written before searches were recorded.
/// </param>
/// <param name="Outcome">
/// What the ThreadWeaver made of the batch's research note, once known. Null while it waits, for a batch with nothing
/// kept (it had no note) and for one completed before outcomes were recorded.
/// </param>
/// <param name="ResolvedAt">When <paramref name="Outcome"/> became known.</param>
public sealed record ResearchBatch(
    string Topic,
    DateTimeOffset SearchedAt,
    IReadOnlyList<BatchResult> Results,
    string? SearchRunId = null,
    IngestionOutcome? Outcome = null,
    DateTimeOffset? ResolvedAt = null);

/// <summary>The author's judgement of a result the Keeper judged.</summary>
public enum ResultMark
{
    /// <summary>Worth passing on for the topic's brief.</summary>
    Useful,

    /// <summary>Noise the pipeline should not have spent anything on.</summary>
    Noise
}

/// <summary>
/// A batch whose kept results were written to a research note, waiting for the ThreadWeaver to file it. The topic stays
/// in flight until its outcome is known.
/// </summary>
/// <param name="NoteFile">The name of the note in the notes folders, by which its run is found.</param>
public sealed record AwaitingBatch(ResearchBatch Batch, string NoteFile, DateTimeOffset NotedAt);
