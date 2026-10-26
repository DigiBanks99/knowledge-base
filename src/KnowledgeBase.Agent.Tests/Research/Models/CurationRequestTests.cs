using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research.Models;

public sealed class CurationRequestTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddHours(1);

    [Test]
    public void ATopicIsRequestedAgainWhileARequestWaits()
    {
        CurationRequest waiting = new("projects/foo", ["projects/foo/index.md", "projects/foo/a.md"], Earlier);

        CurationRequest merged = waiting.Merge(new CurationRequest("projects/foo", ["projects/foo/a.md", "projects/foo/b.md"], Later));

        ItShouldKeepEveryChangedPathOnce();
        ItShouldTakeTheLaterTime();

        void ItShouldKeepEveryChangedPathOnce() =>
            merged.ChangedPaths.ShouldBe(["projects/foo/index.md", "projects/foo/a.md", "projects/foo/b.md"]);

        void ItShouldTakeTheLaterTime() => merged.RequestedAt.ShouldBe(Later);
    }
}
