using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Status;

public enum StatusSignalState
{
    Healthy,
    Degraded,
    Unhealthy
}

/// <summary>One piece of background machinery, such as the model server, and how it was last found.</summary>
public sealed record StatusSignal(string Name, StatusSignalState State, string? Detail);

/// <summary>Every status signal at one point in time. Anything not healthy makes the whole report degraded.</summary>
public sealed record StatusReport(IReadOnlyList<StatusSignal> Signals, bool IsDegraded, DateTimeOffset CheckedAt)
{
    public static StatusReport From(HealthReport report, DateTimeOffset checkedAt)
    {
        StatusSignal[] signals = report.Entries
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new StatusSignal(entry.Key, ToState(entry.Value.Status), entry.Value.Description))
            .ToArray();

        return new StatusReport(signals, signals.Any(signal => signal.State != StatusSignalState.Healthy), checkedAt);
    }

    /// <summary>Whether any signal appeared, disappeared or changed state or detail; when it was checked does not count.</summary>
    public bool HasChangedFrom(StatusReport? previous) =>
        previous is null || !Signals.SequenceEqual(previous.Signals);

    private static StatusSignalState ToState(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => StatusSignalState.Healthy,
        HealthStatus.Degraded => StatusSignalState.Degraded,
        _ => StatusSignalState.Unhealthy
    };
}
