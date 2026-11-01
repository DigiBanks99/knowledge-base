using System.Collections.Concurrent;

using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Research.Search;

/// <summary>What one search of a topic found.</summary>
/// <param name="Found">How many results the sources returned.</param>
/// <param name="Kept">How many survived the filters and wait in the topic's batch.</param>
public sealed record TopicSearchOutcome(string Topic, int Found, int Kept);

/// <summary>
/// Searches one topic: every query against every configured source, the results filtered, the kept ones written as
/// the topic's pending batch and the topic marked searched. Plain code, no model. Every search that runs is recorded
/// as a run, whether it kept something, nothing or failed, so the topic's history shows research happening.
/// </summary>
internal sealed class TopicSearch(
    ILogger<TopicSearch> logger,
    IResearchStore store,
    IEnumerable<ISearchSource> sources,
    ISearchRunLog searchRunLog,
    TimeProvider timeProvider)
{
    // A topic is searched by one caller at a time, so the cycle and the author's "research now" never both write its
    // batch. Other topics are not held up, so "research now" answers while a cycle runs.
    private readonly ConcurrentDictionary<string, byte> _searching = new(StringComparer.Ordinal);

    /// <summary>
    /// The topic's state is read afresh, so a topic muted, pruned or searched since it was chosen is refused rather
    /// than searched again. A refused search sends nothing anywhere, so it is not recorded.
    /// </summary>
    public async Task<Result<TopicSearchOutcome, Error>> SearchAsync(
        string topic,
        SearchStart start,
        CancellationToken cancellationToken)
    {
        if (!_searching.TryAdd(topic, 0))
        {
            return new TopicBeingSearchedError(topic);
        }

        try
        {
            return await store
                .ReadTopicAsync(topic, cancellationToken)
                .Bind(current => current.ToResult<ResearchTopic, Error>(new TopicNotFoundError(topic)))
                .Ensure(current => !current.Muted, current => new TopicMutedError(current.Path))
                .Ensure(current => !current.NeedsReview, current => new TopicNeedsReviewError(current.Path))
                .Ensure(current => current.Queries.Count > 0, current => new TopicNotCuratedError(current.Path))
                .Bind(async current => await store
                    .ListTopicsInFlightAsync(cancellationToken)
                    .Ensure(inFlight => !inFlight.Contains(current.Path), Error (_) => new BatchInFlightError(current.Path))
                    .Map(_ => current))
                .Bind(async current => await store
                    .ReadBlockedDomainsAsync(cancellationToken)
                    .Bind(blocked => SearchAndRecordAsync(current, blocked, start, cancellationToken)));
        }
        finally
        {
            _searching.TryRemove(topic, out _);
        }
    }

    private async Task<Result<TopicSearchOutcome, Error>> SearchAndRecordAsync(
        ResearchTopic topic,
        BlockedDomains blockedDomains,
        SearchStart start,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string runId = SearchRuns.NewId(now);
        IReadOnlyList<SourceCall> calls = await AskAsync(topic, topic.LastSearchedAt ?? now - ResultFilter.FirstSearchWindow, cancellationToken);
        SearchAttempt attempt = await SearchAsync(topic, blockedDomains, start, calls, runId, now, cancellationToken);

        DateTimeOffset completedAt = timeProvider.GetUtcNow();
        AgentRun run = attempt is { Outcome.IsSuccess: true, Filtered: { } filtered }
            ? SearchRuns.Searched(runId, topic.Path, start, now, completedAt, calls, filtered)
            : SearchRuns.Failed(runId, topic.Path, start, now, completedAt, calls, attempt.Filtered, attempt.BatchWritten, attempt.Outcome.Error);
        // By now the search is done and stored; a request cancelled meanwhile must not leave it unrecorded.
        await searchRunLog.RecordAsync(run, CancellationToken.None);
        return attempt.Outcome;
    }

    /// <summary>
    /// The search itself. Once the filters have run, the search is recorded as running before its batch is written, so
    /// the batch never names a run that does not exist and a fetch can add to it at once.
    /// </summary>
    private async Task<SearchAttempt> SearchAsync(
        ResearchTopic topic,
        BlockedDomains blockedDomains,
        SearchStart start,
        IReadOnlyList<SourceCall> calls,
        string runId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Result<bool, Error> complete = Completeness(topic, calls);
        if (complete.IsFailure)
        {
            return new SearchAttempt(complete.Error, null, false);
        }

        (string Query, SearchResult Result)[] found =
        [
            .. calls
                .Where(call => call.Answer.IsSuccess)
                .SelectMany(call => call.Answer.Value.Select(result => (call.Query, result)))
        ];
        FilteredResults filtered = ResultFilter.Apply(found, topic, blockedDomains, now);
        TopicSearchOutcome outcome = new(topic.Path, found.Length, filtered.Kept.Count);

        // The batch is written before the topic is marked searched: a restart in between leaves a batch waiting, never
        // results recorded as seen with nowhere to be judged.
        if (filtered.Kept.Count > 0)
        {
            await searchRunLog.RecordAsync(SearchRuns.Running(runId, topic.Path, start, now, calls), cancellationToken);
            Result<Maybe<ResearchBatch>, Error> written = await store.WritePendingBatchAsync(
                new ResearchBatch(topic.Path, now, filtered.Kept, runId),
                cancellationToken);
            if (written.IsFailure)
            {
                return new SearchAttempt(written.Error, filtered, false);
            }

            if (written.Value.HasNoValue)
            {
                return new SearchAttempt(new TopicNotFoundError(topic.Path), filtered, false);
            }
        }

        // A search some sources failed is not counted as one: the next still asks them for everything since the last
        // complete search, while what was seen this time is still skipped.
        Result<TopicSearchOutcome, Error> searched = await store
            .UpdateTopicAsync(
                topic.Path,
                current => current.HasValue
                    ? Maybe.From(complete.Value ? current.Value.Searched(now, filtered.Seen) : current.Value.Saw(now, filtered.Seen))
                    : Result.Failure<Maybe<ResearchTopic>, Error>(new TopicNotFoundError(topic.Path)),
                cancellationToken)
            .Map(_ => outcome)
            .Tap(result => logger.TopicSearched(result.Topic, result.Found, result.Kept));
        return new SearchAttempt(searched, filtered, filtered.Kept.Count > 0);
    }

    /// <summary>Every query goes to every configured source at once, in query order.</summary>
    private async Task<IReadOnlyList<SourceCall>> AskAsync(ResearchTopic topic, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ISearchSource[] configured = [.. sources.Where(source => source.IsConfigured)];
        return await Task.WhenAll(topic.Queries.SelectMany(query => configured.Select(async source =>
        {
            Result<IReadOnlyList<SearchResult>, Error> answer = await source.SearchAsync(query, since, cancellationToken);
            return new SourceCall(query, source.Name, answer, timeProvider.GetUtcNow());
        })));
    }

    /// <summary>
    /// Whether every source call answered. A call that failed is logged and the others are still used; the search only
    /// fails when every call did, as then nothing is known and the topic should stay due.
    /// </summary>
    private Result<bool, Error> Completeness(ResearchTopic topic, IReadOnlyList<SourceCall> calls)
    {
        if (calls.Count == 0)
        {
            return new SearchSourceError("Research", "no search source is configured.");
        }

        Error[] errors = [.. calls.Where(call => call.Answer.IsFailure).Select(call => call.Answer.Error)];
        foreach (Error error in errors)
        {
            logger.SearchSourceFailed(topic.Path, error);
        }

        if (errors.Length == calls.Count)
        {
            return errors.Length == 1 ? errors[0] : new AggregateError(errors);
        }

        return errors.Length == 0;
    }

    /// <summary>How a search ended, what the filters made of its results if it got that far, and whether its batch was written.</summary>
    private sealed record SearchAttempt(Result<TopicSearchOutcome, Error> Outcome, FilteredResults? Filtered, bool BatchWritten);
}
