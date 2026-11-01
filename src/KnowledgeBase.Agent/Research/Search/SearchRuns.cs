using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Research.Search;

/// <summary>How a topic's search was started.</summary>
public enum SearchStart
{
    /// <summary>The search cycle found the topic due.</summary>
    Scheduled,

    /// <summary>The author asked for it from the Topics page.</summary>
    ResearchNow
}

/// <summary>One query sent to one search source, its answer and when it came.</summary>
internal sealed record SourceCall(
    string Query,
    string Source,
    Result<IReadOnlyList<SearchResult>, Error> Answer,
    DateTimeOffset AnsweredAt);

/// <summary>
/// Builds the run that records a topic's search, so the search shows in the topic's history next to its agent runs. A
/// search is plain code, not an agent: it is recorded as a run of <see cref="Agent"/> under the
/// <see cref="AgentRunTriggerKind.Search"/> trigger, with a line per query and source, and later lines for what became
/// of the results it kept.
/// </summary>
internal static class SearchRuns
{
    /// <summary>The name a search run carries in place of an agent's.</summary>
    public const string Agent = "Search";

    public const string SearchStep = "Search";
    public const string FetchStep = "Fetch";
    public const string KeepStep = "Keep";
    public const string DiscardStep = "Discard";
    public const string NoteStep = "Note";

    public static string NewId(DateTimeOffset at) => AgentRunIds.New(Agent, at);

    /// <summary>
    /// A search whose sources have answered and whose results are about to be stored. It is recorded before its batch
    /// is written, so fetching can add to it straight away, and a restart in between leaves it running for the run
    /// store to fail.
    /// </summary>
    public static AgentRun Running(string id, string topic, SearchStart start, DateTimeOffset startedAt, IReadOnlyList<SourceCall> calls) =>
        new(
            id,
            Agent,
            new AgentRunTrigger(AgentRunTriggerKind.Search, start.ToString()),
            AgentRunStatus.Running,
            startedAt,
            null,
            null,
            null,
            [.. calls.Select(Line)],
            FactOrigin.Research,
            topic);

    /// <summary>A search that ran: what each source answered, what the filters made of it and whether a batch was written.</summary>
    public static AgentRun Searched(
        string id,
        string topic,
        SearchStart start,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IReadOnlyList<SourceCall> calls,
        FilteredResults filtered) =>
        Running(id, topic, start, startedAt, calls) with
        {
            Status = AgentRunStatus.Succeeded,
            CompletedAt = completedAt,
            Summary = $"{Counts(calls, filtered)} {Batch(filtered.Kept.Count, filtered.Kept.Count > 0)}{Failures(calls)}"
        };

    /// <summary>
    /// A search that failed outright: every source call failed, or its results could not be stored. What the filters
    /// made of the results, and whether their batch was written before the failure, is kept in the summary when the
    /// search got that far, as a written batch is still fetched and triaged.
    /// </summary>
    public static AgentRun Failed(
        string id,
        string topic,
        SearchStart start,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IReadOnlyList<SourceCall> calls,
        FilteredResults? filtered,
        bool batchWritten,
        Error error) =>
        Running(id, topic, start, startedAt, calls) with
        {
            Status = AgentRunStatus.Failed,
            CompletedAt = completedAt,
            Summary = filtered is null ? null : $"{Counts(calls, filtered)} {Batch(filtered.Kept.Count, batchWritten)}",
            Error = error.Message
        };

    /// <summary>A line per fetched page: fetched, or why it could not be.</summary>
    public static IReadOnlyList<AgentToolCall> Fetched(IEnumerable<BatchResult> results) =>
    [
        .. results
            .Where(result => result.Page is not null)
            .Select(result => new AgentToolCall(FetchStep, result.Url, result.Page!.FetchedAt, result.Page.Error is null, result.Page.Error))
    ];

    /// <summary>
    /// A line per judged result: kept, or discarded with its reason. A result no verdict could be had for is a failed
    /// discard, as that says nothing about the result itself.
    /// </summary>
    public static IReadOnlyList<AgentToolCall> Judged(IEnumerable<BatchResult> results) =>
    [
        .. results
            .Where(result => result.Verdict is not null)
            .Select(result => Judged(result.Url, result.Verdict!))
    ];

    /// <summary>The line for the research note a batch's kept results became.</summary>
    public static AgentToolCall Noted(string noteFile, DateTimeOffset at) => new(NoteStep, noteFile, at, true, null);

    private static AgentToolCall Judged(string url, ScoutVerdict verdict) => verdict switch
    {
        { Kept: true } => new AgentToolCall(KeepStep, url, verdict.JudgedAt, true, null),
        { Failure: not null } => new AgentToolCall(DiscardStep, url, verdict.JudgedAt, false, verdict.Failure),
        _ => new AgentToolCall(DiscardStep, url, verdict.JudgedAt, true, null, verdict.Reason)
    };

    private static AgentToolCall Line(SourceCall call) => call.Answer.IsSuccess
        ? new AgentToolCall(SearchStep, call.Query, call.AnsweredAt, true, null, $"{call.Source}: {Results(call.Answer.Value.Count)}")
        : new AgentToolCall(SearchStep, call.Query, call.AnsweredAt, false, call.Answer.Error.Message);

    private static string Counts(IReadOnlyList<SourceCall> calls, FilteredResults filtered)
    {
        int found = calls.Where(call => call.Answer.IsSuccess).Sum(call => call.Answer.Value.Count);
        return $"Found {Results(found)}: {filtered.Blocked} blocked, {filtered.AlreadySeen} already seen, {filtered.TooOld} too old, {filtered.Kept.Count} kept.";
    }

    private static string Batch(int kept, bool written) => (kept, written) switch
    {
        (0, _) => "Kept nothing, so no batch was written.",
        (_, true) => $"Wrote a batch of {kept} for triage.",
        _ => "No batch was written."
    };

    private static string Failures(IReadOnlyList<SourceCall> calls)
    {
        int failed = calls.Count(call => call.Answer.IsFailure);
        return failed > 0
            ? $" {failed} of {calls.Count} source calls failed; the next search asks again for everything since the last complete one."
            : string.Empty;
    }

    private static string Results(int count) => count == 1 ? "1 result" : $"{count} results";
}
