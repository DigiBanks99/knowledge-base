using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Fetch;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Fetch;

public sealed class PolitenessGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly PolitenessGate _gate = new(
        Options.Create(new ResearchSettings { FetchPolitenessDelay = TimeSpan.FromSeconds(5) }),
        TimeProvider.System);

    [Test]
    public void AHostIsRequestedForTheFirstTime()
    {
        TimeSpan wait = _gate.Reserve("example.com", Now);

        ItShouldNotWait();

        void ItShouldNotWait() => wait.ShouldBe(TimeSpan.Zero);
    }

    [Test]
    public void AHostIsRequestedThreeTimesAtOnce()
    {
        TimeSpan[] waits = [.. Enumerable.Range(0, 3).Select(_ => _gate.Reserve("example.com", Now))];

        ItShouldSpaceTheRequestsByTheDelay();

        void ItShouldSpaceTheRequestsByTheDelay() =>
            waits.ShouldBe([TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)]);
    }

    [Test]
    public void AHostIsRequestedAgainAfterTheDelay()
    {
        _gate.Reserve("example.com", Now);

        TimeSpan wait = _gate.Reserve("example.com", Now.AddSeconds(6));

        ItShouldNotWait();

        void ItShouldNotWait() => wait.ShouldBe(TimeSpan.Zero);
    }

    [Test]
    public void AnotherHostIsRequested()
    {
        _gate.Reserve("example.com", Now);

        TimeSpan wait = _gate.Reserve("example.org", Now);

        ItShouldNotWait();

        void ItShouldNotWait() => wait.ShouldBe(TimeSpan.Zero);
    }
}
