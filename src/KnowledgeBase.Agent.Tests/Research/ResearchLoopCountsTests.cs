using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research;

public sealed class ResearchLoopCountsTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void TheTopicsAreInEveryState()
    {
        ResearchTopic[] topics =
        [
            Curated("projects/due"),
            Curated("projects/in-flight"),
            Curated("projects/backed-off") with { LastSearchedAt = Now.AddDays(-1), Interval = TimeSpan.FromDays(8) },
            Curated("projects/muted") with { Muted = true },
            Curated("projects/failing") with { LastSearchedAt = Now, ConsecutiveFailures = 3 }
        ];

        ResearchLoopCounts counts = ResearchLoopCounts.Of(topics, new HashSet<string> { "projects/in-flight" }, awaiting: 1, Now);

        ItShouldCountEachState();
        ItShouldDescribeThemWithTheNextCycle();

        void ItShouldCountEachState() => counts.ShouldBe(new ResearchLoopCounts(Due: 1, Awaiting: 1, BackedOff: 1, Muted: 1, Failing: 1));

        void ItShouldDescribeThemWithTheNextCycle() => counts
            .Describe(new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero))
            .ShouldBe("1 due, 1 awaiting, 1 backed off, 1 muted, 1 failing; next cycle 2026-10-28 18:00 UTC.");
    }

    [Test]
    public void NoCycleHasBeenScheduled()
    {
        string description = new ResearchLoopCounts(0, 0, 0, 0, 0).Describe(null);

        ItShouldSaySo();

        void ItShouldSaySo() => description.ShouldEndWith("no cycle scheduled yet.");
    }

    private static ResearchTopic Curated(string path) => ResearchTopic.New(path, Created) with { Queries = ["query"] };
}
