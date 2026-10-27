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

    [Test]
    public void ATopicKeepsChangingWhileItsRequestWaits()
    {
        CurationRequest waiting = new("projects/foo", ["projects/foo/a.md"], Earlier);

        CurationRequest merged = waiting
            .Merge(new CurationRequest("projects/foo", ["projects/foo/b.md"], Later))
            .Merge(new CurationRequest("projects/foo", ["projects/foo/c.md"], Later.AddHours(1)));

        ItShouldKeepItsPlaceInTheQueue();

        void ItShouldKeepItsPlaceInTheQueue() => merged.QueuedSince.ShouldBe(Earlier);
    }

    [Test]
    public void ARequestIsReadBackUnchanged()
    {
        CurationRequest curated = new("projects/foo", ["projects/foo/a.md"], Earlier);

        bool same = curated.IsSameAs(new CurationRequest("projects/foo", ["projects/foo/a.md"], Earlier));

        ItShouldBeTheSameRequest();

        void ItShouldBeTheSameRequest() => same.ShouldBeTrue();
    }

    [Test]
    public void AChangeWasMergedIntoARequest()
    {
        CurationRequest curated = new("projects/foo", ["projects/foo/a.md"], Earlier);

        bool same = curated.IsSameAs(curated.Merge(new CurationRequest("projects/foo", ["projects/foo/b.md"], Later)));

        ItShouldBeANewerRequest();

        void ItShouldBeANewerRequest() => same.ShouldBeFalse();
    }
}
