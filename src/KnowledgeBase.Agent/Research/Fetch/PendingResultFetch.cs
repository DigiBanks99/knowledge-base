using System.Collections.Concurrent;

using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;

namespace KnowledgeBase.Agent.Research.Fetch;

/// <summary>What fetching a topic's pending results did.</summary>
/// <param name="Fetched">Pages fetched and kept for triage.</param>
/// <param name="Failed">Pages that could not be fetched; the reason is recorded on each result.</param>
public sealed record TopicFetchOutcome(string Topic, int Fetched, int Failed);

/// <summary>
/// Fetches the pages of a topic's pending batch that have not been fetched yet. Each page is written back to the batch
/// as soon as it is fetched, so a restart resumes with the pages still missing, and a page that cannot be fetched is
/// recorded with its reason rather than failing the batch. Each page is also added to the run of the search that found
/// it, so the topic's history shows what was fetched.
/// </summary>
internal sealed class PendingResultFetch(
    ILogger<PendingResultFetch> logger,
    IResearchStore store,
    IPageFetcher fetcher,
    ISearchRunLog searchRunLog,
    IHostApplicationLifetime lifetime,
    TimeProvider timeProvider)
{
    // A topic's batch is fetched by one caller at a time, so the cycle and "research now" never fetch a page twice.
    private readonly ConcurrentDictionary<string, byte> _fetching = new(StringComparer.Ordinal);

    /// <summary>
    /// Fetches the topic's pending results in the background, until done or the API stops, for a caller that should
    /// not wait for it.
    /// </summary>
    public void StartFetching(string topic) => _ = Task.Run(async () =>
    {
        try
        {
            await FetchAsync(topic, lifetime.ApplicationStopping).TapError(error => logger.FetchFailed(topic, error));
        }
        catch (Exception ex) when (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            logger.FetchCrashed(topic, ex);
        }
    });

    public async Task<Result<TopicFetchOutcome, Error>> FetchAsync(string topic, CancellationToken cancellationToken)
    {
        if (!_fetching.TryAdd(topic, 0))
        {
            return new TopicFetchOutcome(topic, 0, 0);
        }

        try
        {
            Result<Maybe<ResearchBatch>, Error> batch = await store.ReadPendingBatchAsync(topic, cancellationToken);
            if (batch.IsFailure)
            {
                return batch.Error;
            }

            Result<BlockedDomains, Error> blockedDomains = await store.ReadBlockedDomainsAsync(cancellationToken);
            if (blockedDomains.IsFailure)
            {
                return blockedDomains.Error;
            }

            int fetched = 0;
            int failed = 0;
            string[] waiting = batch.Value.HasValue
                ? [.. batch.Value.Value.Results.Where(result => result.Page is null).Select(result => result.Url)]
                : [];
            foreach (string url in waiting)
            {
                FetchedPage page = await FetchPageAsync(topic, url, blockedDomains.Value, cancellationToken);
                Result<Maybe<ResearchBatch>, Error> written = await store.UpdatePendingBatchAsync(
                    topic,
                    current => current with
                    {
                        Results = [.. current.Results.Select(result => result.Url == url && result.Page is null ? result with { Page = page } : result)]
                    },
                    cancellationToken);
                if (written.IsFailure)
                {
                    return written.Error;
                }

                // The batch was pruned with its topic, or triaged, while its pages were being fetched.
                if (written.Value.HasNoValue)
                {
                    break;
                }

                if (written.Value.Value.SearchRunId is { } searchRunId)
                {
                    await searchRunLog.AppendAsync(
                        searchRunId,
                        SearchRuns.Fetched(written.Value.Value.Results.Where(result => result.Url == url)),
                        cancellationToken);
                }

                if (page.Error is null)
                {
                    fetched++;
                }
                else
                {
                    failed++;
                }
            }

            TopicFetchOutcome outcome = new(topic, fetched, failed);
            if (waiting.Length > 0)
            {
                logger.TopicFetched(topic, fetched, failed);
            }

            return outcome;
        }
        finally
        {
            _fetching.TryRemove(topic, out _);
        }
    }

    /// <summary>
    /// Fetches one page. Anything the fetcher did not foresee (a parser choking on a hostile page) is recorded as that
    /// page's failure, so it is not retried every cycle and never holds up the pages after it.
    /// </summary>
    private async Task<FetchedPage> FetchPageAsync(
        string topic,
        string url,
        BlockedDomains blockedDomains,
        CancellationToken cancellationToken)
    {
        Result<FittedContent, Error> content;
        try
        {
            content = await fetcher.FetchAsync(url, blockedDomains, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.PageFetchCrashed(topic, url, ex);
            return FetchedPage.Failed(timeProvider.GetUtcNow(), $"Could not fetch {url}: {ex.Message}");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (content.IsSuccess)
        {
            return FetchedPage.Content(now, content.Value.Text, content.Value.Truncated);
        }

        logger.PageNotFetched(topic, url, content.Error);
        return FetchedPage.Failed(now, content.Error.Message);
    }
}
