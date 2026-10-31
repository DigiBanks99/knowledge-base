using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research.Models;

public sealed class DueTopicsTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<string> NothingInFlight = [];

    [Test]
    [Arguments(25, true)]
    [Arguments(24, true)]
    [Arguments(23, false)]
    public void ATopicWasSearchedSomeHoursAgo(int hoursAgo, bool due)
    {
        ResearchTopic topic = Curated("projects/atlas") with { LastSearchedAt = Now.AddHours(-hoursAgo) };

        IReadOnlyList<ResearchTopic> selected = DueTopics.Select([topic], NothingInFlight, Now, 10);

        ItShouldBeDueOnceItsIntervalHasPassed();

        void ItShouldBeDueOnceItsIntervalHasPassed() => (selected.Count == 1).ShouldBe(due);
    }

    [Test]
    public void ATopicHasALongerInterval()
    {
        ResearchTopic topic = Curated("projects/atlas") with { LastSearchedAt = Now.AddDays(-3), Interval = TimeSpan.FromDays(4) };

        IReadOnlyList<ResearchTopic> selected = DueTopics.Select([topic], NothingInFlight, Now, 10);

        ItShouldWaitOutItsInterval();

        void ItShouldWaitOutItsInterval() => selected.ShouldBeEmpty();
    }

    [Test]
    public void ATopicHasNeverBeenSearched()
    {
        IReadOnlyList<ResearchTopic> selected = DueTopics.Select([Curated("projects/atlas")], NothingInFlight, Now, 10);

        ItShouldBeDue();

        void ItShouldBeDue() => selected.Select(topic => topic.Path).ShouldBe(["projects/atlas"]);
    }

    [Test]
    public void SomeTopicsCannotBeSearched()
    {
        ResearchTopic[] topics =
        [
            Curated("projects/muted") with { Muted = true },
            ResearchTopic.New("projects/uncurated", Discovered),
            Curated("projects/in-flight"),
            Curated("projects/needs-review") with { ReviewQuestion = "Which Atlas is meant?" },
            Curated("projects/ready")
        ];

        IReadOnlyList<ResearchTopic> selected = DueTopics.Select(topics, new HashSet<string> { "projects/in-flight" }, Now, 10);

        ItShouldSelectOnlyTheOneThatCan();

        void ItShouldSelectOnlyTheOneThatCan() => selected.Select(topic => topic.Path).ShouldBe(["projects/ready"]);
    }

    [Test]
    public void MoreTopicsAreDueThanACycleSearches()
    {
        ResearchTopic[] topics =
        [
            Curated("projects/a") with { LastSearchedAt = Now.AddDays(-2) },
            Curated("projects/b") with { LastSearchedAt = Now.AddDays(-5) },
            Curated("projects/c") with { LastSearchedAt = Now.AddDays(-3) },
            Curated("projects/d")
        ];

        IReadOnlyList<ResearchTopic> selected = DueTopics.Select(topics, NothingInFlight, Now, 3);

        ItShouldTakeTheMostOverdueUpToTheCap();

        // Never searched, it has been due since it was discovered at the start of the month.
        void ItShouldTakeTheMostOverdueUpToTheCap() =>
            selected.Select(topic => topic.Path).ShouldBe(["projects/d", "projects/b", "projects/c"]);
    }

    private static ResearchTopic Curated(string path) =>
        ResearchTopic.New(path, Discovered) with { Brief = "A topic.", Queries = ["a query"] };
}
