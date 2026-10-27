using KnowledgeBase.Agent.Research.Commands;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Jobs;

/// <summary>Runs a curation cycle shortly after start and then once every research cycle.</summary>
internal sealed class CurationJob(
    ILogger<CurationJob> logger,
    IOptions<ResearchSettings> settings,
    TimeProvider timeProvider,
    CurateTopicsCommandHandler handler)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(settings.Value.FirstCycleDelay, timeProvider, stoppingToken);
        using PeriodicTimer timer = new(settings.Value.CycleInterval, timeProvider);
        do
        {
            await handler
                .Handle(new CurateTopicsCommand(), stoppingToken)
                .TapError(logger.CurationCycleFailed);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
