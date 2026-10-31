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
            Curated("projects/failing") with { LastSearchedAt = Now, ConsecutiveFailures = 3 },
            Curated("learning/okf.md") with { ReviewQuestion = "Which OKF is meant?" },
            Curated("learning/muted-question.md") with { Muted = true, ReviewQuestion = "Which one?" }
        ];

        ResearchLoopCounts counts = ResearchLoopCounts.Of(topics, new HashSet<string> { "projects/in-flight" }, awaiting: 1, Now);

        ItShouldCountEachState();
        ItShouldListTheUnmutedTopicsThatNeedReview();
        ItShouldDescribeThemWithTheNextCycle();

        void ItShouldCountEachState() => counts.ShouldSatisfyAllConditions(
            () => counts.Due.ShouldBe(1),
            () => counts.Awaiting.ShouldBe(1),
            () => counts.BackedOff.ShouldBe(1),
            () => counts.Muted.ShouldBe(2),
            () => counts.Failing.ShouldBe(1));

        void ItShouldListTheUnmutedTopicsThatNeedReview() =>
            counts.NeedsReview.ShouldBe([new TopicQuestion("learning/okf.md", "Which OKF is meant?")]);

        void ItShouldDescribeThemWithTheNextCycle() => counts
            .Describe(new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero))
            .ShouldBe("1 due, 1 awaiting, 1 backed off, 2 muted, 1 failing, 1 need review; next cycle 2026-10-28 18:00 UTC. learning/okf.md asks: Which OKF is meant?");
    }

    [Test]
    public void NoCycleHasBeenScheduled()
    {
        string description = new ResearchLoopCounts(0, 0, 0, 0, 0, []).Describe(null);

        ItShouldSaySo();

        void ItShouldSaySo() => description.ShouldEndWith("no cycle scheduled yet.");
    }

    private static ResearchTopic Curated(string path) => ResearchTopic.New(path, Created) with { Queries = ["query"] };
}
