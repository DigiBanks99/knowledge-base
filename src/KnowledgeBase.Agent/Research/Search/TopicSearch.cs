using System.Collections.Concurrent;

using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Search;

/// <summary>What one search of a topic found.</summary>
/// <param name="Found">How many results the sources returned.</param>
/// <param name="Kept">How many survived the filters and wait in the topic's batch.</param>
public sealed record TopicSearchOutcome(string Topic, int Found, int Kept);

/// <summary>
/// Searches one topic: every query against every configured source, the results filtered, the kept ones written as
/// the topic's pending batch and the topic marked searched. Plain code, no model.
/// </summary>
internal sealed class TopicSearch(
    ILogger<TopicSearch> logger,
    IResearchStore store,
    IEnumerable<ISearchSource> sources,
    TimeProvider timeProvider)
{
    // A topic is searched by one caller at a time, so the cycle and the author's "research now" never both write its
    // batch. Other topics are not held up, so "research now" answers while a cycle runs.
    private readonly ConcurrentDictionary<string, byte> _searching = new(StringComparer.Ordinal);

    /// <summary>
    /// The topic's state is read afresh, so a topic muted, pruned or searched since it was chosen is refused rather
    /// than searched again.
    /// </summary>
    public async Task<Result<TopicSearchOutcome, Error>> SearchAsync(string topic, CancellationToken cancellationToken)
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
                .Ensure(current => current.Queries.Count > 0, current => new TopicNotCuratedError(current.Path))
                .Bind(async current => await store
                    .ListTopicsInFlightAsync(cancellationToken)
                    .Ensure(inFlight => !inFlight.Contains(current.Path), Error (_) => new BatchInFlightError(current.Path))
                    .Map(_ => current))
                .Bind(async current => await store
                    .ReadBlockedDomainsAsync(cancellationToken)
                    .Bind(blocked => SearchAsync(current, blocked, cancellationToken)));
        }
        finally
        {
            _searching.TryRemove(topic, out _);
        }
    }

    private async Task<Result<TopicSearchOutcome, Error>> SearchAsync(
        ResearchTopic topic,
        BlockedDomains blockedDomains,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        Result<Findings, Error> found = await FindAsync(
            topic,
            topic.LastSearchedAt ?? now - ResultFilter.FirstSearchWindow,
            cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        FilteredResults filtered = ResultFilter.Apply(found.Value.Results, topic, blockedDomains, now);
        TopicSearchOutcome outcome = new(topic.Path, found.Value.Results.Count, filtered.Kept.Count);

        // The batch is written before the topic is marked searched: a restart in between leaves a batch waiting, never
        // results recorded as seen with nowhere to be judged.
        if (filtered.Kept.Count > 0)
        {
            Result<Maybe<ResearchBatch>, Error> written = await store.WritePendingBatchAsync(
                new ResearchBatch(topic.Path, now, filtered.Kept),
                cancellationToken);
            if (written.IsFailure)
            {
                return written.Error;
            }

            if (written.Value.HasNoValue)
            {
                return new TopicNotFoundError(topic.Path);
            }
        }

        // A search some sources failed is not counted as one: the next still asks them for everything since the last
        // complete search, while what was seen this time is still skipped.
        return await store
            .UpdateTopicAsync(
                topic.Path,
                current => current.HasValue
                    ? Maybe.From(found.Value.Complete ? current.Value.Searched(now, filtered.Seen) : current.Value.Saw(now, filtered.Seen))
                    : Result.Failure<Maybe<ResearchTopic>, Error>(new TopicNotFoundError(topic.Path)),
                cancellationToken)
            .Map(_ => outcome)
            .Tap(searched => logger.TopicSearched(searched.Topic, searched.Found, searched.Kept));
    }

    /// <summary>
    /// Every query goes to every source at once. A call that fails is logged and the others are still used; the search
    /// only fails when every call did, as then nothing is known and the topic should stay due.
    /// </summary>
    private async Task<Result<Findings, Error>> FindAsync(ResearchTopic topic, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ISearchSource[] configured = [.. sources.Where(source => source.IsConfigured)];
        if (configured.Length == 0)
        {
            return new SearchSourceError("Research", "no search source is configured.");
        }

        (string Query, Result<IReadOnlyList<SearchResult>, Error> Answer)[] answers = await Task.WhenAll(topic.Queries
            .SelectMany(query => configured.Select(async source => (query, await source.SearchAsync(query, since, cancellationToken)))));

        Error[] errors = [.. answers.Where(answer => answer.Answer.IsFailure).Select(answer => answer.Answer.Error)];
        foreach (Error error in errors)
        {
            logger.SearchSourceFailed(topic.Path, error);
        }

        if (errors.Length == answers.Length)
        {
            return errors.Length == 1 ? errors[0] : new AggregateError(errors);
        }

        return new Findings(
            [.. answers.Where(answer => answer.Answer.IsSuccess).SelectMany(answer => answer.Answer.Value.Select(result => (answer.Query, result)))],
            errors.Length == 0);
    }

    /// <param name="Complete">Whether every source answered every query.</param>
    private sealed record Findings(IReadOnlyList<(string Query, SearchResult Result)> Results, bool Complete);
}
