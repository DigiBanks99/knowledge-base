using KnowledgeBase.Agent.Status;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Tests.Status;

public sealed class StatusReportTests
{
    private static readonly DateTimeOffset CheckedAt = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);

    [Test]
    public void EverySignalIsHealthy()
    {
        StatusReport report = StatusReport.From(
            Health(("Model server", HealthStatus.Healthy, "Reachable."), ("Backup", HealthStatus.Healthy, "Pushed.")),
            CheckedAt);

        ItShouldNotBeDegraded();
        ItShouldListTheSignalsByName();
        ItShouldKeepEachSignalsDetail();

        void ItShouldNotBeDegraded() => report.IsDegraded.ShouldBeFalse();

        void ItShouldListTheSignalsByName() => report.Signals.Select(signal => signal.Name).ShouldBe(["Backup", "Model server"]);

        void ItShouldKeepEachSignalsDetail() => report.Signals.ShouldBe([
            new StatusSignal("Backup", StatusSignalState.Healthy, "Pushed."),
            new StatusSignal("Model server", StatusSignalState.Healthy, "Reachable.")
        ]);
    }

    [Test]
    [Arguments(HealthStatus.Degraded, StatusSignalState.Degraded)]
    [Arguments(HealthStatus.Unhealthy, StatusSignalState.Unhealthy)]
    public void OneSignalIsNotHealthy(HealthStatus status, StatusSignalState expectedState)
    {
        StatusReport report = StatusReport.From(
            Health(("Model server", status, "Unreachable."), ("Backup", HealthStatus.Healthy, "Pushed.")),
            CheckedAt);

        ItShouldBeDegraded();
        ItShouldReportThatSignalsState();

        void ItShouldBeDegraded() => report.IsDegraded.ShouldBeTrue();

        void ItShouldReportThatSignalsState() =>
            report.Signals.Single(signal => signal.Name == "Model server").State.ShouldBe(expectedState);
    }

    [Test]
    public void NoSignalIsRegistered()
    {
        StatusReport report = StatusReport.From(Health(), CheckedAt);

        ItShouldNotBeDegraded();

        void ItShouldNotBeDegraded() => report.IsDegraded.ShouldBeFalse();
    }

    [Test]
    public void TheSameSignalsAreCheckedAgainLater()
    {
        StatusReport previous = StatusReport.From(Health(("Model server", HealthStatus.Healthy, "Reachable.")), CheckedAt);
        StatusReport current = StatusReport.From(
            Health(("Model server", HealthStatus.Healthy, "Reachable.")),
            CheckedAt.AddSeconds(30));

        bool changed = current.HasChangedFrom(previous);

        ItShouldNotCountAsAChange();

        void ItShouldNotCountAsAChange() => changed.ShouldBeFalse();
    }

    [Test]
    [Arguments(HealthStatus.Degraded, "Reachable.")]
    [Arguments(HealthStatus.Healthy, "Reachable at another address.")]
    public void ASignalChanges(HealthStatus status, string detail)
    {
        StatusReport previous = StatusReport.From(Health(("Model server", HealthStatus.Healthy, "Reachable.")), CheckedAt);
        StatusReport current = StatusReport.From(Health(("Model server", status, detail)), CheckedAt.AddSeconds(30));

        bool changed = current.HasChangedFrom(previous);

        ItShouldCountAsAChange();

        void ItShouldCountAsAChange() => changed.ShouldBeTrue();
    }

    [Test]
    public void ASignalIsAdded()
    {
        StatusReport previous = StatusReport.From(Health(("Model server", HealthStatus.Healthy, "Reachable.")), CheckedAt);
        StatusReport current = StatusReport.From(
            Health(("Model server", HealthStatus.Healthy, "Reachable."), ("Backup", HealthStatus.Healthy, "Pushed.")),
            CheckedAt);

        bool changed = current.HasChangedFrom(previous);
        bool firstReportChanged = previous.HasChangedFrom(null);

        ItShouldCountAsAChange();
        ItShouldCountTheFirstReportAsAChange();

        void ItShouldCountAsAChange() => changed.ShouldBeTrue();

        void ItShouldCountTheFirstReportAsAChange() => firstReportChanged.ShouldBeTrue();
    }

    private static HealthReport Health(params (string Name, HealthStatus Status, string Detail)[] entries) =>
        new(
            entries.ToDictionary(
                entry => entry.Name,
                entry => new HealthReportEntry(entry.Status, entry.Detail, TimeSpan.Zero, null, null)),
            TimeSpan.Zero);
}