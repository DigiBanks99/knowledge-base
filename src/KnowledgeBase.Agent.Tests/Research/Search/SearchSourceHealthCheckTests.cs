using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Status;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Tests.Research.Search;

public sealed class SearchSourceHealthCheckTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly ISearchSource _source = Substitute.For<ISearchSource>();
    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly SearchSourceHealthCheck<ISearchSource> _check;
    private readonly HealthCheckContext _context = new()
    {
        Registration = new HealthCheckRegistration(
            "Search: Hacker News",
            Substitute.For<IHealthCheck>(),
            HealthStatus.Degraded,
            [StatusDependencyInjection.StatusTag])
    };

    public SearchSourceHealthCheckTests()
    {
        _source.Name.Returns("Hacker News");
        _source.IsConfigured.Returns(true);
        _source.ProbeAsync(Arg.Any<CancellationToken>()).Returns(UnitResult.Success<Error>());
        _timeProvider.GetUtcNow().Returns(Now);
        _check = new SearchSourceHealthCheck<ISearchSource>(_source, _timeProvider);
    }

    public void Dispose() => _check.Dispose();

    [Test]
    public async Task TheSourceAnswers()
    {
        HealthCheckResult result = await _check.CheckHealthAsync(_context);

        ItShouldBeHealthy();

        void ItShouldBeHealthy() => result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Test]
    public async Task TheSourceIsUnreachable()
    {
        _source.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(new SearchSourceError("Hacker News", "connection refused")));

        HealthCheckResult result = await _check.CheckHealthAsync(_context);

        ItShouldBeDegradedWithTheReason();

        void ItShouldBeDegradedWithTheReason() => result.ShouldSatisfyAllConditions(
            () => result.Status.ShouldBe(HealthStatus.Degraded),
            () => result.Description.ShouldBe("Hacker News could not be searched: connection refused"));
    }

    [Test]
    public async Task TheSourceIsNotConfigured()
    {
        _source.IsConfigured.Returns(false);

        HealthCheckResult result = await _check.CheckHealthAsync(_context);

        ItShouldBeHealthyAsUnused();
        ItShouldNotProbeIt();

        void ItShouldBeHealthyAsUnused() => result.ShouldSatisfyAllConditions(
            () => result.Status.ShouldBe(HealthStatus.Healthy),
            () => result.Description.ShouldBe("Not used; Hacker News is not configured."));

        void ItShouldNotProbeIt() => _source.DidNotReceive().ProbeAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TheStatusIsCheckedAgainSoonAfter()
    {
        await _check.CheckHealthAsync(_context);
        _timeProvider.GetUtcNow().Returns(Now.AddMinutes(4));
        await _check.CheckHealthAsync(_context);

        ItShouldReuseTheLastProbe();

        void ItShouldReuseTheLastProbe() => _source.Received(1).ProbeAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TheStatusIsCheckedAgainLater()
    {
        await _check.CheckHealthAsync(_context);
        _timeProvider.GetUtcNow().Returns(Now.AddMinutes(5));
        await _check.CheckHealthAsync(_context);

        ItShouldProbeAgain();

        void ItShouldProbeAgain() => _source.Received(2).ProbeAsync(Arg.Any<CancellationToken>());
    }
}
