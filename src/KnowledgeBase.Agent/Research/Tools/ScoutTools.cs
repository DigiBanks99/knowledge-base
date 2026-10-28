using System.ComponentModel;

using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Research.Tools;

/// <summary>
/// The Scout's only tools: keep the result it was given, or discard it. A triage run is for one result, which it
/// finds from the run (the Scout never names it), and the verdict is stored on that result as the tool is called.
/// </summary>
internal sealed class ScoutTools(AgentRunTracker runTracker, IResearchStore store, TimeProvider timeProvider)
{
    internal const int MaxSummaryLength = 800;
    internal const int MaxQuotes = 3;
    internal const int MaxQuoteLength = 300;
    internal const int MaxReasonLength = 300;

    // The limits are spelled out because attribute arguments cannot interpolate the constants above.
    [Description("""
                 Keeps the result: it is worth passing on for the topic. Call this when the page tells something new and relevant to the brief, and also when you are unsure.
                 The summary is two to four sentences in your own words (at most 800 characters) saying what the page tells that is relevant to the topic.
                 Give up to 3 short quotes copied exactly from the page (each at most 300 characters; longer ones are cut).
                 """)]
    public async Task<string> KeepResult(
        [Description("Two to four sentences in your own words on what the page tells that is relevant to the topic.")] string summary,
        [Description("Up to 3 short quotes copied exactly from the page; may be empty.")] string[] quotes,
        CancellationToken cancellationToken)
    {
        string cleanSummary = Tidy(summary, MaxSummaryLength);
        if (cleanSummary.Length == 0)
        {
            throw new InvalidOperationException("The summary is empty. Call `KeepResult` again with a summary of what the page tells that is relevant to the topic.");
        }

        string[] cleanQuotes =
        [
            .. (quotes ?? [])
                .Select(quote => Tidy(quote, MaxQuoteLength))
                .Where(quote => quote.Length > 0)
                .Take(MaxQuotes)
        ];
        await RecordAsync(ScoutVerdict.Keep(cleanSummary, cleanQuotes, timeProvider.GetUtcNow()), cancellationToken);
        return "Kept the result. You are done.";
    }

    [Description("""
                 Discards the result: it is noise for the topic, such as a rant, a beginner question, SEO spam, a restatement of the basics or an unrelated page. Never discard a result you are unsure about; keep it instead.
                 """)]
    public async Task<string> DiscardResult(
        [Description("One sentence on why the result is not worth passing on.")] string reason,
        CancellationToken cancellationToken)
    {
        string cleanReason = Tidy(reason, MaxReasonLength);
        if (cleanReason.Length == 0)
        {
            throw new InvalidOperationException("The reason is empty. Call `DiscardResult` again with one sentence on why the result is not worth passing on.");
        }

        await RecordAsync(ScoutVerdict.Discard(cleanReason, timeProvider.GetUtcNow()), cancellationToken);
        return "Discarded the result. You are done.";
    }

    /// <summary>
    /// What the model wrote reaches a note the pipeline reads, so it is made one line of bounded length: it cannot
    /// carry markdown structure or a header of its own into the note.
    /// </summary>
    internal static string Tidy(string? text, int limit)
    {
        string line = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return line.Length <= limit ? line : $"{line[..(limit - 1)].TrimEnd()}…";
    }

    private async Task RecordAsync(ScoutVerdict verdict, CancellationToken cancellationToken)
    {
        AgentRun run = runTracker.Current is { Trigger.Kind: AgentRunTriggerKind.Triage, Topic: not null } current
            ? current
            : throw new InvalidOperationException("There is no result to judge: this tool only works during a triage run.");

        string? refusal = null;
        Result<Maybe<ResearchBatch>, Error> updated = await store.UpdatePendingBatchAsync(
            run.Topic!,
            batch =>
            {
                BatchResult? result = batch.Results.FirstOrDefault(result => result.Url == run.Trigger.Reference);
                if (result is null)
                {
                    refusal = "The result you were given is no longer waiting to be judged.";
                    return batch;
                }

                if (result.Verdict is not null)
                {
                    refusal = "You have already given a verdict for this result. Reply with one short sentence; do not call a tool again.";
                    return batch;
                }

                return batch with
                {
                    Results = [.. batch.Results.Select(other => ReferenceEquals(other, result) ? result with { Verdict = verdict } : other)]
                };
            },
            cancellationToken);
        if (updated.IsFailure)
        {
            throw new InvalidOperationException(updated.Error.Message);
        }

        if (refusal is not null || updated.Value.HasNoValue)
        {
            throw new InvalidOperationException(refusal ?? "The result you were given is no longer waiting to be judged.");
        }
    }
}
