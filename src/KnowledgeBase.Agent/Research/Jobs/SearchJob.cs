using KnowledgeBase.Agent.Research.Commands;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Jobs;

/// <summary>
/// Runs a search cycle shortly after start, then again <see cref="ResearchSettings.SearchCycleInterval"/> after each
/// one finishes, so cycles never overlap however long one takes.
/// </summary>
internal sealed class SearchJob(
    ILogger<SearchJob> logger,
    IOptions<ResearchSettings> settings,
    TimeProvider timeProvider,
    SearchTopicsCommandHandler handler)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(settings.Value.FirstCycleDelay, timeProvider, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            // A bug in one cycle must not end research for good, nor (as an exception escaping a background service
            // does) stop the whole API.
            try
            {
                await handler
                    .Handle(new SearchTopicsCommand(), stoppingToken)
                    .TapError(logger.SearchCycleFailed);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.SearchCycleCrashed(ex);
            }

            await Task.Delay(settings.Value.SearchCycleInterval, timeProvider, stoppingToken);
        }
    }
}
