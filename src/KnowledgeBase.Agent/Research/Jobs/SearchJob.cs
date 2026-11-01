using KnowledgeBase.Agent.Research.Commands;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Jobs;

/// <summary>
/// Runs a search cycle shortly after start, then again <see cref="ResearchSettings.SearchCycleInterval"/> after each
/// one finishes, so cycles never overlap however long one takes. A cycle first settles the batches awaiting the
/// ThreadWeaver, so their topics' intervals are current, then searches the due topics, fetches the pages of every
/// pending batch, including any a restart interrupted, and has the Keeper judge the fetched results and the Clerk
/// summarise the kept ones.
/// </summary>
internal sealed class SearchJob(
    ILogger<SearchJob> logger,
    IOptions<ResearchSettings> settings,
    TimeProvider timeProvider,
    ResolveAwaitingBatchesCommandHandler resolveHandler,
    SearchTopicsCommandHandler handler,
    FetchPendingResultsCommandHandler fetchHandler,
    TriageResultsCommandHandler triageHandler,
    ResearchCycleClock cycleClock)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        cycleClock.NextCycle(timeProvider.GetUtcNow() + settings.Value.FirstCycleDelay);
        await Task.Delay(settings.Value.FirstCycleDelay, timeProvider, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            // A bug in one cycle must not end research for good, nor (as an exception escaping a background service
            // does) stop the whole API.
            try
            {
                await resolveHandler
                    .Handle(new ResolveAwaitingBatchesCommand(), stoppingToken)
                    .TapError(logger.ResolveCycleFailed);
                await handler
                    .Handle(new SearchTopicsCommand(), stoppingToken)
                    .TapError(logger.SearchCycleFailed);
                await fetchHandler
                    .Handle(new FetchPendingResultsCommand(), stoppingToken)
                    .TapError(logger.FetchCycleFailed);
                await triageHandler
                    .Handle(new TriageResultsCommand(), stoppingToken)
                    .TapError(logger.TriageCycleFailed);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.SearchCycleCrashed(ex);
            }

            cycleClock.NextCycle(timeProvider.GetUtcNow() + settings.Value.SearchCycleInterval);
            await Task.Delay(settings.Value.SearchCycleInterval, timeProvider, stoppingToken);
        }
    }
}
