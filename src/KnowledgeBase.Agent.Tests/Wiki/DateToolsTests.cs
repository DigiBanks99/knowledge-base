using KnowledgeBase.Agent.Wiki.Tools;

namespace KnowledgeBase.Agent.Tests.Wiki;

public sealed class DateToolsTests
{
    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly DateTools _tools;

    public DateToolsTests()
    {
        _tools = new DateTools(_timeProvider);
    }

    [Test]
    public void AnAgentAsksForTheTime()
    {
        _timeProvider.GetUtcNow().Returns(new DateTimeOffset(2026, 10, 24, 10, 15, 30, TimeSpan.Zero));

        string now = _tools.GetCurrentDateTimeIso();

        ItShouldAnswerInRoundTripIsoFormat();

        void ItShouldAnswerInRoundTripIsoFormat() => now.ShouldBe("2026-10-24T10:15:30.0000000+00:00");
    }
}
