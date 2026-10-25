using KnowledgeBase.Agent.Review.Commands;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Review.Jobs;

internal sealed class ScheduledReviewJob(
    ILogger<ScheduledReviewJob> logger,
    IOptions<ReviewSettings> settings,
    TimeProvider timeProvider,
    RequestReviewWikiCommandHandler requestReviewHandler)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(settings.Value.Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await requestReviewHandler
                .Handle(new RequestReviewWikiCommand(timeProvider.GetUtcNow()), stoppingToken)
                .Tap(logger.ScheduledReviewRequested)
                .TapError(logger.ErrorOccurred);
        }
    }
}
