using KnowledgeBase.Agent.Events;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Status;

/// <summary>
/// Receives the status signals each time the health check publisher runs them, keeps the latest report for the
/// Status page and tells the UI's event stream when anything changed.
/// </summary>
internal sealed class StatusMonitor(
    ILogger<StatusMonitor> logger,
    IChangePublisher changes,
    HealthCheckService healthChecks,
    TimeProvider timeProvider)
    : IHealthCheckPublisher
{
    public const string Area = "status";

    private readonly Lock _lock = new();
    private StatusReport? _latest;
    private Task<StatusReport>? _firstCheck;

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        Record(StatusReport.From(report, timeProvider.GetUtcNow()));
        return Task.CompletedTask;
    }

    /// <summary>The latest report, checking the signals now if the publisher has not run them yet.</summary>
    public Task<StatusReport> GetAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            // Requests that arrive before the first publish share one check rather than each probing every signal.
            return _latest is not null
                ? Task.FromResult(_latest)
                : (_firstCheck ??= CheckNowAsync()).WaitAsync(cancellationToken);
        }
    }

    private async Task<StatusReport> CheckNowAsync()
    {
        // Not the caller's token: the check is shared, so one request going away must not cancel it for the others.
        HealthReport health = await healthChecks.CheckHealthAsync(StatusDependencyInjection.IsStatusSignal, CancellationToken.None);
        // The publisher may have recorded a newer report while this check ran; that one wins.
        return Record(StatusReport.From(health, timeProvider.GetUtcNow()), unlessRecorded: true);
    }

    private StatusReport Record(StatusReport report, bool unlessRecorded = false)
    {
        StatusReport? previous;
        lock (_lock)
        {
            if (unlessRecorded && _latest is not null)
            {
                return _latest;
            }

            previous = _latest;
            _latest = report;
        }

        if (!report.HasChangedFrom(previous))
        {
            return report;
        }

        foreach (StatusSignal signal in report.Signals.Except(previous?.Signals ?? []))
        {
            logger.StatusSignalChanged(signal.Name, signal.State, signal.Detail);
        }

        changes.Publish(Area);
        return report;
    }
}