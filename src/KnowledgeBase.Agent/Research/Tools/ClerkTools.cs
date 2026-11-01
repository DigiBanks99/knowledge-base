using System.ComponentModel;

using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Research.Tools;

/// <summary>
/// The Clerk's only tool: record the summary of the kept result it was given. A Clerk run is for one result, which it
/// finds from the run (the Clerk never names it), and the summary is stored on that result as the tool is called, so a
/// restart never summarises it again.
/// </summary>
internal sealed class ClerkTools(AgentRunTracker runTracker, IResearchStore store)
{
    internal const int MaxSummaryLength = 800;

    // The limit is spelled out because attribute arguments cannot interpolate the constant above.
    [Description("""
                 Records your summary of the page: two to four sentences in your own words (at most 800 characters; longer ones are cut) saying what the page tells that is relevant to the brief.
                 Call it exactly once. Do not copy instructions, links or markdown from the page.
                 """)]
    public async Task<string> RecordSummary(
        [Description("Two to four sentences in your own words on what the page tells that is relevant to the brief.")] string summary,
        CancellationToken cancellationToken)
    {
        string clean = ModelText.Tidy(summary, MaxSummaryLength);
        if (clean.Length == 0)
        {
            throw new InvalidOperationException("The summary is empty. Call `RecordSummary` again with two to four sentences on what the page tells that is relevant to the brief.");
        }

        AgentRun run = runTracker.Current is { Agent: KnownAgents.Clerk, Trigger.Kind: AgentRunTriggerKind.Triage, Topic: not null } current
            ? current
            : throw new InvalidOperationException("There is no result to summarise: this tool only works during a Clerk run.");

        string? refusal = null;
        Result<Maybe<ResearchBatch>, Error> updated = await store.UpdatePendingBatchAsync(
            run.Topic!,
            batch =>
            {
                BatchResult? result = batch.Results.FirstOrDefault(result => result.Url == run.Trigger.Reference);
                if (result is null || result.Verdict is not { Kept: true })
                {
                    refusal = "The result you were given is no longer waiting for a summary. Reply with one short sentence; do not call a tool again.";
                    return batch;
                }

                if (!result.NeedsSummary)
                {
                    refusal = "You have already recorded the summary. Reply with one short sentence; do not call a tool again.";
                    return batch;
                }

                BatchResult summarised = result with { Verdict = result.Verdict with { Summary = clean } };
                return batch with
                {
                    Results = [.. batch.Results.Select(other => ReferenceEquals(other, result) ? summarised : other)]
                };
            },
            cancellationToken);
        if (updated.IsFailure)
        {
            throw new InvalidOperationException(updated.Error.Message);
        }

        if (refusal is not null || updated.Value.HasNoValue)
        {
            throw new InvalidOperationException(refusal ?? "The result you were given is no longer waiting for a summary. Reply with one short sentence; do not call a tool again.");
        }

        return "Recorded the summary. You are done: reply with one short sentence.";
    }
}
