using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Curation;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Fetch;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Status;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>
/// Has the Keeper judge the fetched results of the pending batches and the Clerk summarise the kept ones: one research
/// cycle's worth of triage.
/// </summary>
public record struct TriageResultsCommand;

/// <summary>What one triage cycle did.</summary>
/// <param name="Skipped">Whether the cycle was skipped: triage is disabled or no model server was healthy.</param>
/// <param name="Judged">Results given a verdict this cycle.</param>
/// <param name="Awaiting">Topics whose kept results were written to a note, now awaiting ingestion.</param>
/// <param name="Completed">Topics whose results were all discarded, so their batch completed as empty.</param>
/// <param name="Summarised">Kept results the Clerk summarised this cycle.</param>
public sealed record TriageCycle(
    bool Skipped,
    int Judged,
    IReadOnlyList<string> Awaiting,
    IReadOnlyList<string> Completed,
    int Summarised = 0)
{
    public static TriageCycle SkippedCycle { get; } = new(true, 0, [], []);
}

internal sealed class TriageResultsCommandHandler(
    ILogger<TriageResultsCommandHandler> logger,
    IOptions<ResearchSettings> settings,
    IResearchStore store,
    IResearchNoteWriter noteWriter,
    IModelServerHealth modelServer,
    ContentBudget contentBudget,
    IPromptTokenCounter tokenCounter,
    ModelGate modelGate,
    AgentRunTracker runTracker,
    ISearchRunLog searchRunLog,
    IKeeper keeper,
    [FromKeyedServices(KnownAgents.Clerk)] AIAgent clerk,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    private readonly ResearchSettings _settings = settings.Value;

    // Sent with every prompt, so it counts towards the budget too. The tool schema and chat template add a little more
    // that is not counted, which the budget's headroom below the context size covers.
    private readonly string _instructions = AgentInstructions.ComposeRoleOnly(
        KnownAgents.Clerk,
        configuration[$"{KnownAgents.Clerk}:{nameof(AgentModelOptions.Instructions)}"] ?? string.Empty);

    /// <summary>
    /// Judges results, the oldest batch first, once a batch's pages are all fetched, until
    /// <see cref="ResearchSettings.KeeperCallsPerCycle"/> Keeper calls are spent; then has the Clerk summarise the kept
    /// results, until <see cref="ResearchSettings.ClerkRunsPerCycle"/> runs are spent. A batch whose results are all
    /// judged and whose kept results all have a summary or a failed Clerk run is finished: its kept results become one
    /// note, or, with none kept, it completes as empty. Nothing runs while triage is disabled (a dry run), and neither
    /// the Keeper nor the Clerk runs while its model server is unhealthy, so an outage doesn't use up attempts.
    /// </summary>
    public async Task<Result<TriageCycle, Error>> Handle(TriageResultsCommand _, CancellationToken cancellationToken)
    {
        if (!_settings.TriageEnabled)
        {
            return TriageCycle.SkippedCycle;
        }

        bool keeperAvailable = await modelServer.IsKeeperServerAvailableAsync(cancellationToken);
        bool clerkAvailable = await modelServer.IsAvailableToAsync(KnownAgents.Clerk, cancellationToken);
        if (!keeperAvailable && !clerkAvailable)
        {
            logger.TriageSkippedModelServerUnhealthy();
            return TriageCycle.SkippedCycle;
        }

        if (!keeperAvailable)
        {
            logger.KeeperSkippedModelServerUnhealthy();
        }

        if (!clerkAvailable)
        {
            logger.ClerkSkippedModelServerUnhealthy();
        }

        return await store
            .ListPendingBatchesAsync(cancellationToken)
            .Bind(batches => TriageAsync([.. batches.OrderBy(batch => batch.SearchedAt)], keeperAvailable, clerkAvailable, cancellationToken));
    }

    /// <summary>
    /// The Keeper judges every ready batch before the Clerk summarises any: its calls are quick and decide what is worth
    /// the Clerk's far slower runs, so a cycle never spends its model time summarising while results wait to be judged.
    /// </summary>
    private async Task<Result<TriageCycle, Error>> TriageAsync(
        IReadOnlyList<ResearchBatch> batches,
        bool keeperAvailable,
        bool clerkAvailable,
        CancellationToken cancellationToken)
    {
        CallBudget calls = new(Math.Max(0, _settings.KeeperCallsPerCycle));
        CallBudget clerkRuns = new(Math.Max(0, _settings.ClerkRunsPerCycle));
        int judged = 0;
        int summarised = 0;
        List<string> awaiting = [];
        List<string> completed = [];
        List<Error> errors = [];
        List<(ResearchBatch Batch, ResearchTopic Topic)> ready = [];
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

            ready.Add((listed, topic.Value));
        }

        foreach ((ResearchBatch listed, ResearchTopic topic) in keeperAvailable ? ready : [])
        {
            foreach (BatchResult result in listed.Results.Where(result => result.Verdict is null))
            {
                // A page that was never fetched costs the model nothing, so it is judged whatever is left.
                if (calls.Left <= 0 && result.Page!.Error is null)
                {
                    break;
                }

                Result<bool, Error> done = await JudgeAsync(topic, result, calls, cancellationToken);
                if (done.IsFailure)
                {
                    errors.Add(done.Error);
                    continue;
                }

                if (done.Value)
                {
                    judged++;
                }
            }
        }

        foreach ((ResearchBatch listed, ResearchTopic topic) in ready)
        {
            if (clerkAvailable)
            {
                Result<int, Error> written = await SummariseAsync(topic, clerkRuns, cancellationToken);
                if (written.IsFailure)
                {
                    errors.Add(written.Error);
                }
                else
                {
                    summarised += written.Value;
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
            0 => new TriageCycle(false, judged, awaiting, completed, summarised),
            1 => errors[0],
            _ => new AggregateError(errors)
        };
    }

    /// <summary>
    /// The Keeper calls (or Clerk runs) a cycle has left. Every call counts as it is made, answered or not, so neither a
    /// model that keeps timing out nor a store that cannot record the answers lets a cycle run past its cap. A kept
    /// result's second Keeper call may take the cycle one over rather than waste the first.
    /// </summary>
    private sealed class CallBudget(int left)
    {
        public int Left { get; private set; } = left;

        public void Spend() => Left--;
    }

    /// <summary>
    /// Judges one result in up to two Keeper calls: the first decides whether it is kept, and only a kept result is
    /// asked the second, for its kind and quotes. A call that fails leaves the result waiting for the next cycle, until
    /// it has failed <see cref="ResearchSettings.KeeperMaxAttempts"/> times. Says whether the result got a verdict.
    /// </summary>
    private async Task<Result<bool, Error>> JudgeAsync(ResearchTopic topic, BatchResult result, CallBudget calls, CancellationToken cancellationToken)
    {
        if (result.Page!.Error is not null)
        {
            return await RecordAsync(topic.Path, result.Url, Verdict.Discard($"The page could not be fetched: {result.Page.Error}", timeProvider.GetUtcNow()), cancellationToken);
        }

        KeeperPage page = await PageAsync(topic, result, cancellationToken);
        if (page.Paragraphs.Count == 0)
        {
            return await RecordAsync(topic.Path, result.Url, Verdict.Discard("The page has no prose paragraphs to judge.", timeProvider.GetUtcNow()), cancellationToken);
        }

        calls.Spend();
        Result<Screening, Error> screening = await CallAsync(
            topic,
            result.Url,
            token => keeper.ScreenAsync(page, token),
            answer => KeeperPolicy.Summarise(answer, Decide(answer)),
            cancellationToken);
        if (screening.IsFailure)
        {
            return await FailedAttemptAsync(topic.Path, result, screening.Error, Verdict.Failed, cancellationToken);
        }

        KeeperDecision decision = Decide(screening.Value);
        if (!decision.Kept)
        {
            Verdict discard = Verdict.Discard(decision.Reason!, timeProvider.GetUtcNow()) with
            {
                Useful = screening.Value.Useful,
                Injection = screening.Value.Injection
            };
            return await RecordAsync(topic.Path, result.Url, discard, cancellationToken);
        }

        Verdict kept = Verdict.Keep(null, [], timeProvider.GetUtcNow()) with
        {
            Useful = screening.Value.Useful,
            Injection = screening.Value.Injection
        };
        calls.Spend();
        Result<Description, Error> description = await CallAsync(
            topic,
            result.Url,
            token => keeper.DescribeAsync(page, token),
            answer => KeeperPolicy.Summarise(answer, page.Paragraphs.Count, _settings.QuoteProbabilityFloor),
            cancellationToken);
        if (description.IsFailure)
        {
            // The result was already found worth keeping, so giving up on its description keeps it without one.
            return await FailedAttemptAsync(topic.Path, result, description.Error, (_, judgedAt) => kept with { JudgedAt = judgedAt }, cancellationToken);
        }

        Verdict described = kept with
        {
            Quotes = KeeperPolicy.Quotes(description.Value.QuoteProbabilities, page.Paragraphs, _settings.QuoteProbabilityFloor),
            Kind = description.Value.Kind,
            KindProbability = description.Value.KindProbability
        };
        return await RecordAsync(topic.Path, result.Url, described, cancellationToken);
    }

    private KeeperDecision Decide(Screening screening) =>
        KeeperPolicy.Judge(screening, _settings.KeepThreshold, _settings.InjectionThreshold);

    /// <summary>
    /// The page as the Keeper reads it: its prose paragraphs, cut to <see cref="ResearchSettings.KeeperContentTokenBudget"/>
    /// at a paragraph, since every question rereads the whole page and latency grows with it.
    /// </summary>
    private async Task<KeeperPage> PageAsync(ResearchTopic topic, BatchResult result, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> paragraphs = KeeperParagraphs.From(result.Page!.Markdown ?? string.Empty);
        FittedContent fitted = await contentBudget.FitAsync(string.Join("\n\n", paragraphs), _settings.KeeperContentTokenBudget, cancellationToken);
        IReadOnlyList<string> sent = fitted.Truncated
            ? [.. fitted.Text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)]
            : paragraphs;
        return new KeeperPage(
            string.IsNullOrWhiteSpace(topic.Brief) ? topic.Path : topic.Brief,
            result.Result.Title,
            result.Url,
            sent,
            Math.Max(0, _settings.MaxQuoteOptions));
    }

    /// <summary>
    /// Makes one Keeper call through the model gate, ahead of Pleb runs since the Keeper and the chat model share the
    /// CPU, and records it as a run of the topic with no tool calls, whose summary holds the answer's probabilities.
    /// </summary>
    private async Task<Result<T, Error>> CallAsync<T>(
        ResearchTopic topic,
        string url,
        Func<CancellationToken, Task<Result<T, Error>>> call,
        Func<T, string> summarise,
        CancellationToken cancellationToken)
    {
        Result<T, Error>? answer = null;
        Result<AgentRun, Error> run = await modelGate.RunAsync(
            token => runTracker.RunAsync(
                KeeperPolicy.Agent,
                new AgentRunTrigger(AgentRunTriggerKind.Triage, url),
                FactOrigin.Research,
                topic.Path,
                async runToken =>
                {
                    answer = await call(runToken);
                    return answer.Value.IsSuccess
                        ? new AgentResponse(new ChatMessage(ChatRole.Assistant, summarise(answer.Value.Value)))
                        : throw new InvalidOperationException(answer.Value.Error.Message);
                },
                token),
            cancellationToken,
            ModelPriority.High);

        // The Keeper's own error says more than the run's, which wraps it.
        if (answer is { IsFailure: true } failed)
        {
            return failed.Error;
        }

        return run.IsFailure ? run.Error : answer!.Value;
    }

    /// <summary>
    /// Counts a failed call against the result. Once it has failed too often the result is given up on with the verdict
    /// <paramref name="giveUp"/> makes of the failure, so its batch can finish.
    /// </summary>
    private async Task<Result<bool, Error>> FailedAttemptAsync(
        string topic,
        BatchResult result,
        Error error,
        Func<string, DateTimeOffset, Verdict> giveUp,
        CancellationToken cancellationToken)
    {
        int attempts = result.Attempts + 1;
        if (attempts < Math.Max(1, _settings.KeeperMaxAttempts))
        {
            logger.ResultLeftWaiting(topic, result.Url, error);
            return await store
                .UpdatePendingBatchAsync(topic, batch => Replace(batch, result.Url, waiting => waiting with { Attempts = attempts }), cancellationToken)
                .Map(_ => false);
        }

        string failure = $"The Keeper failed {attempts} times: {error.Message}";
        logger.ResultNotJudged(topic, result.Url, failure);
        return await RecordAsync(topic, result.Url, giveUp(failure, timeProvider.GetUtcNow()), cancellationToken);
    }

    /// <summary>
    /// Runs the Clerk once for each of the topic's kept results that still needs a summary, while
    /// <paramref name="runs"/> last. The batch is read afresh, so results the Keeper kept this cycle are included and
    /// those summarised before a restart are skipped. Says how many got a summary.
    /// </summary>
    private async Task<Result<int, Error>> SummariseAsync(ResearchTopic topic, CallBudget runs, CancellationToken cancellationToken)
    {
        Result<Maybe<ResearchBatch>, Error> read = await store.ReadPendingBatchAsync(topic.Path, cancellationToken);
        if (read.IsFailure)
        {
            return read.Error;
        }

        int summarised = 0;
        List<Error> errors = [];
        foreach (BatchResult result in read.Value.GetValueOrDefault()?.Results.Where(result => result.NeedsSummary) ?? [])
        {
            if (runs.Left <= 0)
            {
                break;
            }

            Result<bool, Error> done = await SummariseAsync(topic, result, runs, cancellationToken);
            if (done.IsFailure)
            {
                errors.Add(done.Error);
            }
            else if (done.Value)
            {
                summarised++;
            }
        }

        return errors.Count switch
        {
            0 => summarised,
            1 => errors[0],
            _ => new AggregateError(errors)
        };
    }

    /// <summary>
    /// One Clerk run for one kept result, which says whether the result got a summary. The summary is stored as the tool
    /// is called. A run that ends without one, or a prompt over <see cref="ResearchSettings.ClerkPromptTokenBudget"/>,
    /// gives the result up: it keeps its quotes alone. When the prompt cannot be counted, the model timed out or the
    /// server could not be reached, the fault is the server's, so the result waits for the next cycle, until that has
    /// happened <see cref="ResearchSettings.ClerkMaxAttempts"/> times. Only a run that reaches the model is charged to
    /// <paramref name="runs"/>.
    /// </summary>
    private async Task<Result<bool, Error>> SummariseAsync(ResearchTopic topic, BatchResult result, CallBudget runs, CancellationToken cancellationToken)
    {
        string prompt = ClerkPrompt.Build(topic.Brief, result.Result.Title, result.Result.Url, result.Page?.Markdown ?? string.Empty);
        Result<int, Error> tokens = await tokenCounter.CountAsync($"{_instructions}\n{prompt}", cancellationToken);
        if (tokens.IsFailure)
        {
            return await LeaveWaitingAsync(topic.Path, result, tokens.Error, cancellationToken);
        }

        if (tokens.Value > _settings.ClerkPromptTokenBudget)
        {
            return await GiveUpSummaryAsync(
                topic.Path,
                result.Url,
                $"The Clerk prompt is {tokens.Value} tokens, over the budget of {_settings.ClerkPromptTokenBudget}. Lower {ResearchSettings.SectionName}:{nameof(ResearchSettings.TriageContentTokenBudget)} or raise {ResearchSettings.SectionName}:{nameof(ResearchSettings.ClerkPromptTokenBudget)}.",
                cancellationToken);
        }

        runs.Spend();
        bool serverFault = false;
        Result<AgentRun, Error> run = await modelGate.RunAsync(
            token => runTracker.RunAsync(
                KnownAgents.Clerk,
                new AgentRunTrigger(AgentRunTriggerKind.Triage, result.Url),
                FactOrigin.Research,
                topic.Path,
                async runToken =>
                {
                    AgentResponse response;
                    try
                    {
                        response = await clerk.RunAsync(prompt, cancellationToken: runToken);
                    }
                    catch (Exception ex) when (IsServerFault(ex) && !runToken.IsCancellationRequested)
                    {
                        serverFault = true;
                        throw;
                    }

                    return HasSummary(runTracker.Current)
                        ? response
                        : throw new InvalidOperationException(
                            $"The Clerk ended without a summary: it must call `{nameof(ClerkTools.RecordSummary)}` successfully.");
                },
                token),
            cancellationToken,
            ModelPriority.Medium);

        // The summary is stored as the tool is called, so a run that fails afterwards has still summarised the result.
        Result<Maybe<ResearchBatch>, Error> current = await store.ReadPendingBatchAsync(topic.Path, cancellationToken);
        if (current.IsFailure)
        {
            return current.Error;
        }

        BatchResult? now = current.Value.GetValueOrDefault()?.Results.FirstOrDefault(other => other.Url == result.Url);
        if (now is null || !now.NeedsSummary)
        {
            return now?.Verdict?.Summary is not null;
        }

        return serverFault
            ? await LeaveWaitingAsync(topic.Path, now, new ClerkUnavailableError(RunError(run)), cancellationToken)
            : await GiveUpSummaryAsync(topic.Path, result.Url, RunError(run), cancellationToken);
    }

    /// <summary>
    /// Counts a run the server could not serve against the result, which waits for the next cycle until it has failed
    /// <see cref="ResearchSettings.ClerkMaxAttempts"/> times and is then given up on, so its batch can finish.
    /// </summary>
    private async Task<Result<bool, Error>> LeaveWaitingAsync(string topic, BatchResult result, Error error, CancellationToken cancellationToken)
    {
        int attempts = result.SummaryAttempts + 1;
        if (attempts < Math.Max(1, _settings.ClerkMaxAttempts))
        {
            logger.SummaryLeftWaiting(topic, result.Url, error);
            return await store
                .UpdatePendingBatchAsync(topic, batch => ReplaceUnsummarised(batch, result.Url, waiting => waiting with { SummaryAttempts = attempts }), cancellationToken)
                .Map(_ => false);
        }

        return await GiveUpSummaryAsync(topic, result.Url, $"The Clerk could not run {attempts} times: {error.Message}", cancellationToken);
    }

    /// <summary>The run's own error, without the run tracker's wrapping, as the result's reason for having no summary.</summary>
    private static string RunError(Result<AgentRun, Error> run) => run switch
    {
        { IsSuccess: true } => "The Clerk ended without a summary.",
        { Error: AgentRunFailedError { Run.Error: { } error } } => error,
        _ => run.Error.Message
    };

    /// <summary>Records that a kept result gets no summary, so its batch can finish with the result's quotes alone.</summary>
    private async Task<Result<bool, Error>> GiveUpSummaryAsync(string topic, string url, string failure, CancellationToken cancellationToken)
    {
        logger.SummaryNotWritten(topic, url, failure);
        return await store
            .UpdatePendingBatchAsync(
                topic,
                batch => ReplaceUnsummarised(batch, url, result => result with { Verdict = result.Verdict! with { SummaryFailure = failure } }),
                cancellationToken)
            .Map(_ => false);
    }

    private enum Finish
    {
        Waiting,
        Awaiting,
        Completed
    }

    /// <summary>
    /// Finishes a batch whose results are all triaged (judged, and each kept one summarised or given up on): its kept
    /// results become one note and the batch awaits the ThreadWeaver's outcome; with none kept it completes as empty.
    /// The note is written before the batch moves, and never twice, so a restart in between only moves the batch.
    /// </summary>
    private async Task<Result<Finish, Error>> FinishAsync(string topic, CancellationToken cancellationToken)
    {
        Result<Maybe<ResearchBatch>, Error> read = await store.ReadPendingBatchAsync(topic, cancellationToken);
        if (read.IsFailure)
        {
            return read.Error;
        }

        if (read.Value.HasNoValue || read.Value.Value.Results.Any(result => !result.Triaged))
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

    /// <summary>Records a verdict for a result still waiting for one; whether it did says whether the result was judged.</summary>
    private async Task<Result<bool, Error>> RecordAsync(string topic, string url, Verdict verdict, CancellationToken cancellationToken) =>
        await store
            .UpdatePendingBatchAsync(topic, batch => Replace(batch, url, result => result with { Verdict = verdict }), cancellationToken)
            .Map(batch => batch.HasValue);

    private static ResearchBatch Replace(ResearchBatch batch, string url, Func<BatchResult, BatchResult> change) => batch with
    {
        Results = [.. batch.Results.Select(result => result.Url == url && result.Verdict is null ? change(result) : result)]
    };

    /// <summary>Changes a kept result still waiting for the Clerk, so a summary written meanwhile is never overwritten.</summary>
    private static ResearchBatch ReplaceUnsummarised(ResearchBatch batch, string url, Func<BatchResult, BatchResult> change) => batch with
    {
        Results = [.. batch.Results.Select(result => result.Url == url && result.NeedsSummary ? change(result) : result)]
    };

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

    private static bool HasSummary(AgentRun? run) =>
        run?.ToolCalls.Any(call => call.Succeeded && call.Tool == nameof(ClerkTools.RecordSummary)) == true;

    /// <summary>
    /// The server timed out or could not be reached (it restarted or is reloading its model): no fault of the result's,
    /// so it is tried again rather than given up on.
    /// </summary>
    private static bool IsServerFault(Exception ex) =>
        ex is TimeoutException or TaskCanceledException or HttpRequestException
        || ex.InnerException is TimeoutException or HttpRequestException;
}
