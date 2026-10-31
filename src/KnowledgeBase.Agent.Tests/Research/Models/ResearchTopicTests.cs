using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research.Models;

public sealed class ResearchTopicTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly ResearchTopic _topic = ResearchTopic.New("projects/atlas", Now.AddDays(-30));

    [Test]
    public void ATopicKeepsComingUpEmpty()
    {
        List<TimeSpan> intervals = [];
        ResearchTopic topic = _topic;
        for (int round = 0; round < 7; round++)
        {
            topic = topic.Empty();
            intervals.Add(topic.Interval!.Value);
        }

        ItShouldDoubleTheIntervalUpToThirtyDays();

        void ItShouldDoubleTheIntervalUpToThirtyDays() => intervals.ShouldBe(
        [
            TimeSpan.FromHours(48),
            TimeSpan.FromDays(4),
            TimeSpan.FromDays(8),
            TimeSpan.FromDays(16),
            TimeSpan.FromDays(30),
            TimeSpan.FromDays(30),
            TimeSpan.FromDays(30)
        ]);
    }

    [Test]
    public void ABackedOffTopicHasSomethingFiled()
    {
        ResearchTopic backedOff = _topic with { Interval = TimeSpan.FromDays(16), ConsecutiveFailures = 2 };

        ResearchTopic positive = backedOff.Positive(Now);

        ItShouldBeCheckedDailyAgain();
        ItShouldRememberWhen();
        ItShouldForgetTheFailures();

        void ItShouldBeCheckedDailyAgain() => positive.Interval.ShouldBe(TimeSpan.FromHours(24));
        void ItShouldRememberWhen() => positive.LastPositiveAt.ShouldBe(Now);
        void ItShouldForgetTheFailures() => positive.ConsecutiveFailures.ShouldBe(0);
    }

    [Test]
    public void ANoteFailsThreeTimesRunning()
    {
        ResearchTopic backedOff = _topic with { Interval = TimeSpan.FromDays(4) };

        ResearchTopic twice = backedOff.Failed().Failed();
        ResearchTopic thrice = twice.Failed();

        ItShouldLeaveTheIntervalAlone();
        ItShouldNotFlagTwoFailures();
        ItShouldFlagThree();

        void ItShouldLeaveTheIntervalAlone() => thrice.Interval.ShouldBe(TimeSpan.FromDays(4));
        void ItShouldNotFlagTwoFailures() => twice.IsFailing.ShouldBeFalse();
        void ItShouldFlagThree() => thrice.IsFailing.ShouldBeTrue();
    }

    [Test]
    public void AFailingTopicComesUpEmpty()
    {
        ResearchTopic failing = _topic.Failed().Failed().Failed();

        ResearchTopic quiet = failing.Empty();

        ItShouldNoLongerBeFailing();

        void ItShouldNoLongerBeFailing() => quiet.IsFailing.ShouldBeFalse();
    }

    [Test]
    public void SomeSeenUrlsAreOlderThanAHundredAndEightyDays()
    {
        ResearchTopic topic = _topic
            .Saw(Now.AddDays(-200), ["https://example.com/old"])
            .Saw(Now.AddDays(-10), ["https://example.com/recent"]);

        ResearchTopic pruned = topic.Saw(Now, ["https://example.com/new"]);

        ItShouldForgetTheOldOne();
        ItShouldKeepTheOthers();

        void ItShouldForgetTheOldOne() => pruned.HasSeen("https://example.com/old").ShouldBeFalse();
        void ItShouldKeepTheOthers() => pruned.SeenUrls!.Keys.ShouldBe(["https://example.com/recent", "https://example.com/new"], ignoreOrder: true);
    }

    [Test]
    public void NoSeenUrlHasExpired()
    {
        ResearchTopic topic = _topic.Saw(Now.AddDays(-10), ["https://example.com/recent"]);

        ResearchTopic pruned = topic.WithoutExpiredUrls(Now);

        ItShouldBeTheSameTopic();

        void ItShouldBeTheSameTopic() => pruned.ShouldBeSameAs(topic);
    }

    [Test]
    public void TheAuthorGuidesATopicMoreOftenThanItKeeps()
    {
        ResearchTopic guided = _topic;
        foreach (int day in new[] { 1, 2, 3, 4 })
        {
            guided = guided.WithGuidance(new AuthorGuidance(GuidanceKind.Rejection, $"Reason {day}.", Now.AddDays(day)), limit: 3);
        }

        ItShouldKeepOnlyTheLatestEntries();

        void ItShouldKeepOnlyTheLatestEntries() =>
            guided.Guidance.ShouldNotBeNull().Select(entry => entry.Text).ShouldBe(["Reason 2.", "Reason 3.", "Reason 4."]);
    }
}
