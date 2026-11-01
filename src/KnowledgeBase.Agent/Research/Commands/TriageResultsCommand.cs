using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Curation;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Status;

using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>Has the Scout judge the fetched results of the pending batches: one research cycle's worth of triage.</summary>
public record struct TriageResultsCommand;

/// <summary>What one triage cycle did.</summary>
/// <param name="Skipped">Whether the cycle was skipped: the Scout is disabled or the model server was not healthy.</param>
/// <param name="Judged">Results given a verdict this cycle.</param>
/// <param name="Awaiting">Topics whose kept results were written to a note, now awaiting ingestion.</param>
/// <param name="Completed">Topics whose results were all discarded, so their batch completed as empty.</param>
public sealed record TriageCycle(
    bool Skipped,
    int Judged,
    IReadOnlyList<string> Awaiting,
    IReadOnlyList<string> Completed)
{
    public static TriageCycle SkippedCycle { get; } = new(true, 0, [], []);
}

internal sealed class TriageResultsCommandHandler(
    ILogger<TriageResultsCommandHandler> logger,
    IOptions<ResearchSettings> settings,
    IResearchStore store,
    IResearchNoteWriter noteWriter,
    IModelServerHealth modelServer,
    IPromptTokenCounter tokenCounter,
    ModelGate modelGate,
    AgentRunTracker runTracker,
    ISearchRunLog searchRunLog,
    [FromKeyedServices(KnownAgents.Scout)] AIAgent scout,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    private static readonly HashSet<string> VerdictTools = [nameof(ScoutTools.KeepResult), nameof(ScoutTools.DiscardResult)];

    private readonly ResearchSettings _settings = settings.Value;

    // Sent with every prompt, so it counts towards the budget too.
    private readonly string _instructions = AgentInstructions.ComposeRoleOnly(
        KnownAgents.Scout,
        configuration[$"{KnownAgents.Scout}:{nameof(AgentModelOptions.Instructions)}"] ?? string.Empty);

    /// <summary>
    /// Judges up to <see cref="ResearchSettings.ScoutRunsPerCycle"/> results, the oldest batch first, once a batch's pages
    /// are all fetched. A batch whose results are all judged is finished: its kept results become one note, or, with
    /// none kept, it completes as empty. Nothing runs while the Scout is disabled (a dry run) or the model server is
    /// unhealthy, so an outage doesn't turn the results into failures.
    /// </summary>
    public async Task<Result<TriageCycle, Error>> Handle(TriageResultsCommand _, CancellationToken cancellationToken)
    {
        if (!_settings.ScoutEnabled)
        {
            return TriageCycle.SkippedCycle;
        }

        if (!await modelServer.IsAvailableToAsync(KnownAgents.Scout, cancellationToken))
        {
            logger.TriageSkippedModelServerUnhealthy();
            return TriageCycle.SkippedCycle;
        }

        return await store
            .ListPendingBatchesAsync(cancellationToken)
            .Bind(batches => TriageAsync([.. batches.OrderBy(batch => batch.SearchedAt)], cancellationToken));
    }

    private async Task<Result<TriageCycle, Error>> TriageAsync(IReadOnlyList<ResearchBatch> batches, CancellationToken cancellationToken)
    {
        int runsLeft = Math.Max(0, _settings.ScoutRunsPerCycle);
        int judged = 0;
        List<string> awaiting = [];
        List<string> completed = [];
        List<Error> errors = [];
        foreach (ResearchBatch listed in batches)
        {
            // Pages are still being fetched; judging half a batch would only delay its note.
            if (listed.Results.Any(result => result.Page is null))
            {
                continue;
            }

            Result<ResearchTopic?, Error> topic = await store
                .ReadTopicAsync(listed.Topic, cancellationToken)
                .Map(maybe => maybe.GetValueOrDefault());
            if (topic.IsFailure || topic.Value is null)
            {
                // A pruned topic takes its batch with it; a topic that cannot be read is tried again next cycle.
                if (topic.IsFailure)
                {
                    errors.Add(topic.Error);
                }

                continue;
            }

            foreach (BatchResult result in listed.Results.Where(result => result.Verdict is null))
            {
                if (runsLeft <= 0)
                {
                    break;
                }

                Result<bool, Error> done = await JudgeAsync(topic.Value, result, cancellationToken);
                if (done.IsFailure)
                {
                    errors.Add(done.Error);
                    continue;
                }

                // Every attempt to run the Scout counts against the cap, so a model that keeps timing out cannot hold up
                // the cycle; a page that was never fetched costs the model nothing.
                if (result.Page!.Error is null)
                {
                    runsLeft--;
                }

                if (done.Value)
                {
                    judged++;
                }
            }

            Result<Finish, Error> finished = await FinishAsync(listed.Topic, cancellationToken);
            if (finished.IsFailure)
            {
                errors.Add(finished.Error);
            }
            else if (finished.Value == Finish.Awaiting)
            {
                awaiting.Add(listed.Topic);
            }
            else if (finished.Value == Finish.Completed)
            {
                completed.Add(listed.Topic);
            }
        }

        return errors.Count switch
        {
            0 => new TriageCycle(false, judged, awaiting, completed),
            1 => errors[0],
            _ => new AggregateError(errors)
        };
    }

    /// <summary>
    /// Records a verdict for one result and says whether it got one. A result that could not be judged because the model
    /// timed out gets none, and is tried again next cycle; every other way of not getting a verdict is recorded as a
    /// discard, so a confused model never stalls its batch.
    /// </summary>
    private async Task<Result<bool, Error>> JudgeAsync(ResearchTopic topic, BatchResult result, CancellationToken cancellationToken)
    {
        if (result.Page!.Error is not null)
        {
            return await RecordAsync(topic.Path, result.Url, ScoutVerdict.Discard($"The page could not be fetched: {result.Page.Error}", timeProvider.GetUtcNow()), cancellationToken);
        }

        string prompt = ScoutPrompt.Build(topic.Path, topic.Brief, result.Result.Title, result.Result.Url, result.Page.Markdown ?? string.Empty);
        Result<int, Error> tokens = await tokenCounter.CountAsync($"{_instructions}\n{prompt}", cancellationToken);
        if (tokens.IsFailure)
        {
            logger.ResultLeftWaiting(topic.Path, result.Url, tokens.Error);
            return false;
        }

        if (tokens.Value > _settings.ScoutPromptTokenBudget)
        {
            string failure = $"The Scout prompt is {tokens.Value} tokens, over the budget of {_settings.ScoutPromptTokenBudget}. Lower {ResearchSettings.SectionName}:{nameof(ResearchSettings.TriageContentTokenBudget)} or raise {ResearchSettings.SectionName}:{nameof(ResearchSettings.ScoutPromptTokenBudget)}.";
            logger.ResultNotJudged(topic.Path, result.Url, failure);
            return await RecordAsync(topic.Path, result.Url, ScoutVerdict.Failed(failure, timeProvider.GetUtcNow()), cancellationToken);
        }

        bool timedOut = false;
        Result<AgentRun, Error> run = await modelGate.RunAsync(
            token => runTracker.RunAsync(
                KnownAgents.Scout,
                new AgentRunTrigger(AgentRunTriggerKind.Triage, result.Url),
                FactOrigin.Research,
                topic.Path,
                async runToken =>
                {
                    AgentResponse response;
                    try
                    {
                        response = await scout.RunAsync(prompt, cancellationToken: runToken);
                    }
                    catch (Exception ex) when (IsTimeout(ex) && !runToken.IsCancellationRequested)
                    {
                        timedOut = true;
                        throw;
                    }

                    return HasVerdict(runTracker.Current)
                        ? response
                        : throw new InvalidOperationException(
                            $"The Scout ended without a verdict: it must call `{nameof(ScoutTools.KeepResult)}` or `{nameof(ScoutTools.DiscardResult)}` successfully.");
                },
                token),
            cancellationToken,
            ModelPriority.High);

        // The verdict is stored as the tool is called, so a run that fails afterwards has still judged the result.
        Result<Maybe<ResearchBatch>, Error> current = await store.ReadPendingBatchAsync(topic.Path, cancellationToken);
        if (current.IsFailure)
        {
            return current.Error;
        }

        BatchResult? now = current.Value.GetValueOrDefault()?.Results.FirstOrDefault(other => other.Url == result.Url);
        if (current.Value.HasNoValue || now is null || now.Verdict is not null)
        {
            return now?.Verdict is not null;
        }

        if (timedOut)
        {
            logger.ResultLeftWaiting(topic.Path, result.Url, run.Error);
            return false;
        }

        string reason = run.IsFailure ? run.Error.Message : "The Scout ended without a verdict.";
        logger.ResultNotJudged(topic.Path, result.Url, reason);
        return await RecordAsync(topic.Path, result.Url, ScoutVerdict.Failed(reason, timeProvider.GetUtcNow()), cancellationToken);
    }

    private enum Finish
    {
        Waiting,
        Awaiting,
        Completed
    }

    /// <summary>
    /// Finishes a batch whose results are all judged: its kept results become one note and the batch awaits the
    /// ThreadWeaver's outcome; with none kept it completes as empty. The note is written before the batch moves, and
    /// never twice, so a restart in between only moves the batch.
    /// </summary>
    private async Task<Result<Finish, Error>> FinishAsync(string topic, CancellationToken cancellationToken)
    {
        Result<Maybe<ResearchBatch>, Error> read = await store.ReadPendingBatchAsync(topic, cancellationToken);
        if (read.IsFailure)
        {
            return read.Error;
        }

        if (read.Value.HasNoValue || read.Value.Value.Results.Any(result => result.Verdict is null))
        {
            return Finish.Waiting;
        }

        ResearchBatch batch = read.Value.Value;
        BatchResult[] kept = [.. batch.Results.Where(result => result.Verdict!.Kept)];
        if (kept.Length == 0)
        {
            logger.TopicTriagedEmpty(topic, batch.Results.Count);
            await AppendToSearchRunAsync(batch, [], cancellationToken);
            // Nothing to ingest, so there is no outcome to wait for: the topic backs off now, as for a note that filed nothing.
            return await store
                .UpdateTopicAsync(topic, current => Result.Success<Maybe<ResearchTopic>, Error>(current.Select(existing => existing.Empty())), cancellationToken)
                .Bind(_ => store.CompleteBatchAsync(topic, cancellationToken))
                .Map(_ => Finish.Completed);
        }

        ResearchNote note = ResearchNote.Build(batch, kept);
        if (!noteWriter.Exists(note))
        {
            try
            {
                await noteWriter.WriteAsync(note, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new ResearchStoreError(note.FileName, ex.Message);
            }
        }

        logger.TopicTriaged(topic, kept.Length, batch.Results.Count, note.FileName);
        await AppendToSearchRunAsync(batch, [SearchRuns.Noted(note.FileName, timeProvider.GetUtcNow())], cancellationToken);
        return await store
            .AwaitBatchAsync(topic, note.FileName, timeProvider.GetUtcNow(), cancellationToken)
            .Map(_ => Finish.Awaiting);
    }

    private async Task<Result<bool, Error>> RecordAsync(string topic, string url, ScoutVerdict verdict, CancellationToken cancellationToken) =>
        await store
            .UpdatePendingBatchAsync(
                topic,
                batch => batch with
                {
                    Results = [.. batch.Results.Select(result => result.Url == url && result.Verdict is null ? result with { Verdict = verdict } : result)]
                },
                cancellationToken)
            .Map(batch => batch.HasValue);

    /// <summary>
    /// Adds the batch's pages, verdicts and any <paramref name="more"/> (its note) to the run of the search that found
    /// it, before the batch moves on, so a finish retried after a failure adds them again and the log skips what it
    /// already has. The pages are added again too: one fetched just before a restart may have missed its line.
    /// </summary>
    private async Task AppendToSearchRunAsync(ResearchBatch batch, IReadOnlyList<AgentToolCall> more, CancellationToken cancellationToken)
    {
        if (batch.SearchRunId is { } searchRunId)
        {
            await searchRunLog.AppendAsync(
                searchRunId,
                [.. SearchRuns.Fetched(batch.Results), .. SearchRuns.Judged(batch.Results), .. more],
                cancellationToken);
        }
    }

    private static bool HasVerdict(AgentRun? run) =>
        run?.ToolCalls.Any(call => call.Succeeded && VerdictTools.Contains(call.Tool)) == true;

    private static bool IsTimeout(Exception ex) =>
        ex is TimeoutException or TaskCanceledException || ex.InnerException is TimeoutException;
}
