using KnowledgeBase.Agent.Events;
using KnowledgeBase.Agent.Status;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Status;

public sealed class StatusMonitorTests
{
    private readonly IChangePublisher _changes = Substitute.For<IChangePublisher>();
    private readonly HealthCheckService _healthChecks = Substitute.For<HealthCheckService>();
    private readonly StatusMonitor _monitor;

    public StatusMonitorTests()
    {
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(new DateTimeOffset(2026, 10, 25, 9, 30, 0, TimeSpan.Zero));

        _monitor = new StatusMonitor(NullLogger<StatusMonitor>.Instance, _changes, _healthChecks, timeProvider);
    }

    [Test]
    public async Task TheSignalsArePublishedForTheFirstTime()
    {
        await _monitor.PublishAsync(Health(HealthStatus.Healthy, "Reachable."), CancellationToken.None);
        StatusReport report = await _monitor.GetAsync(CancellationToken.None);

        ItShouldTellTheEventStream();
        ItShouldServeThePublishedReport();
        await ItShouldNotCheckTheSignalsAgain();

        void ItShouldTellTheEventStream() => _changes.Received(1).Publish(StatusMonitor.Area);

        void ItShouldServeThePublishedReport() =>
            report.Signals.ShouldBe([new StatusSignal("Model server", StatusSignalState.Healthy, "Reachable.")]);

        async Task ItShouldNotCheckTheSignalsAgain() => await _healthChecks
            .DidNotReceiveWithAnyArgs()
            .CheckHealthAsync(null, CancellationToken.None);
    }

    [Test]
    public async Task TheSignalsAreUnchangedWhenPublishedAgain()
    {
        await _monitor.PublishAsync(Health(HealthStatus.Healthy, "Reachable."), CancellationToken.None);
        await _monitor.PublishAsync(Health(HealthStatus.Healthy, "Reachable."), CancellationToken.None);

        ItShouldTellTheEventStreamOnlyOnce();

        void ItShouldTellTheEventStreamOnlyOnce() => _changes.Received(1).Publish(StatusMonitor.Area);
    }

    [Test]
    public async Task TheModelServerGoesDown()
    {
        await _monitor.PublishAsync(Health(HealthStatus.Healthy, "Reachable."), CancellationToken.None);
        await _monitor.PublishAsync(Health(HealthStatus.Degraded, "Unreachable."), CancellationToken.None);
        StatusReport report = await _monitor.GetAsync(CancellationToken.None);

        ItShouldTellTheEventStreamAgain();
        ItShouldServeTheDegradedReport();

        void ItShouldTellTheEventStreamAgain() => _changes.Received(2).Publish(StatusMonitor.Area);

        void ItShouldServeTheDegradedReport() => report.IsDegraded.ShouldBeTrue();
    }

    [Test]
    public async Task TheStatusIsRequestedBeforeTheSignalsWerePublished()
    {
        _healthChecks
            .CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>())
            .Returns(Health(HealthStatus.Degraded, "Unreachable."));

        StatusReport report = await _monitor.GetAsync(CancellationToken.None);

        ItShouldCheckTheSignalsNow();
        ItShouldTellTheEventStream();

        void ItShouldCheckTheSignalsNow() =>
            report.Signals.ShouldBe([new StatusSignal("Model server", StatusSignalState.Degraded, "Unreachable.")]);

        void ItShouldTellTheEventStream() => _changes.Received(1).Publish(StatusMonitor.Area);
    }

    [Test]
    public async Task SeveralRequestsArriveBeforeTheSignalsWerePublished()
    {
        TaskCompletionSource<HealthReport> check = new();
        _healthChecks
            .CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>())
            .Returns(check.Task);

        Task<StatusReport> first = _monitor.GetAsync(CancellationToken.None);
        Task<StatusReport> second = _monitor.GetAsync(CancellationToken.None);
        check.SetResult(Health(HealthStatus.Healthy, "Reachable."));
        StatusReport[] reports = await Task.WhenAll(first, second);

        await ItShouldCheckTheSignalsOnce();
        ItShouldAnswerBothWithTheSameReport();

        async Task ItShouldCheckTheSignalsOnce() => await _healthChecks
            .Received(1)
            .CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>());

        void ItShouldAnswerBothWithTheSameReport() => reports[1].ShouldBeSameAs(reports[0]);
    }

    [Test]
    public async Task TheSignalsArePublishedWhileARequestChecksThem()
    {
        TaskCompletionSource<HealthReport> check = new();
        _healthChecks
            .CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>())
            .Returns(check.Task);

        Task<StatusReport> request = _monitor.GetAsync(CancellationToken.None);
        await _monitor.PublishAsync(Health(HealthStatus.Healthy, "Reachable."), CancellationToken.None);
        check.SetResult(Health(HealthStatus.Degraded, "Unreachable."));
        StatusReport answered = await request;
        StatusReport latest = await _monitor.GetAsync(CancellationToken.None);

        ItShouldKeepThePublishedReport();
        ItShouldAnswerTheRequestWithIt();
        ItShouldTellTheEventStreamOnlyOnce();

        void ItShouldKeepThePublishedReport() => latest.IsDegraded.ShouldBeFalse();

        void ItShouldAnswerTheRequestWithIt() => answered.ShouldBeSameAs(latest);

        void ItShouldTellTheEventStreamOnlyOnce() => _changes.Received(1).Publish(StatusMonitor.Area);
    }

    private static HealthReport Health(HealthStatus status, string detail) =>
        new(
            new Dictionary<string, HealthReportEntry>
            {
                ["Model server"] = new(status, detail, TimeSpan.Zero, null, null)
            },
            TimeSpan.Zero);
}