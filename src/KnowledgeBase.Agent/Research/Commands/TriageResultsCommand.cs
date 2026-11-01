using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Fetch;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Status;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>Has the Keeper judge the fetched results of the pending batches: one research cycle's worth of triage.</summary>
public record struct TriageResultsCommand;

/// <summary>What one triage cycle did.</summary>
/// <param name="Skipped">Whether the cycle was skipped: triage is disabled or the model server was not healthy.</param>
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
    ContentBudget contentBudget,
    ModelGate modelGate,
    AgentRunTracker runTracker,
    ISearchRunLog searchRunLog,
    IKeeper keeper,
    TimeProvider timeProvider)
{
    private readonly ResearchSettings _settings = settings.Value;

    /// <summary>
    /// Judges results, the oldest batch first, once a batch's pages are all fetched, until
    /// <see cref="ResearchSettings.KeeperCallsPerCycle"/> Keeper calls are spent. A batch whose results are all judged
    /// is finished: its kept results become one note, or, with none kept, it completes as empty. Nothing runs while
    /// triage is disabled (a dry run) or the Keeper's server is unhealthy, so an outage doesn't use up attempts.
    /// </summary>
    public async Task<Result<TriageCycle, Error>> Handle(TriageResultsCommand _, CancellationToken cancellationToken)
    {
        if (!_settings.TriageEnabled || !_settings.ScoutEnabled)
        {
            return TriageCycle.SkippedCycle;
        }

        if (!await modelServer.IsKeeperServerAvailableAsync(cancellationToken))
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
        CallBudget calls = new(Math.Max(0, _settings.KeeperCallsPerCycle));
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
                // A page that was never fetched costs the model nothing, so it is judged whatever is left.
                if (calls.Left <= 0 && result.Page!.Error is null)
                {
                    break;
                }

                Result<bool, Error> done = await JudgeAsync(topic.Value, result, calls, cancellationToken);
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
    /// The Keeper calls a cycle has left. Every call counts as it is made, answered or not, so neither a model that
    /// keeps timing out nor a store that cannot record the answers lets a cycle run past its cap. A kept result's second
    /// call may take the cycle one over rather than waste the first.
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

    /// <summary>Records a verdict for a result still waiting for one; whether it did says whether the result was judged.</summary>
    private async Task<Result<bool, Error>> RecordAsync(string topic, string url, Verdict verdict, CancellationToken cancellationToken) =>
        await store
            .UpdatePendingBatchAsync(topic, batch => Replace(batch, url, result => result with { Verdict = verdict }), cancellationToken)
            .Map(batch => batch.HasValue);

    private static ResearchBatch Replace(ResearchBatch batch, string url, Func<BatchResult, BatchResult> change) => batch with
    {
        Results = [.. batch.Results.Select(result => result.Url == url && result.Verdict is null ? change(result) : result)]
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
}
