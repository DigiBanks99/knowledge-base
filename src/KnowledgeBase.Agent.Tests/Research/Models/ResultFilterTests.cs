using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research.Models;

public sealed class ResultFilterTests
{
    private const string Query = "atlas framework release";

    private static readonly DateTimeOffset Discovered = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastSearched = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly ResearchTopic _topic = ResearchTopic.New("projects/atlas", Discovered) with
    {
        Queries = [Query],
        LastSearchedAt = LastSearched,
        SeenUrls = new Dictionary<string, DateTimeOffset> { ["https://example.com/seen"] = LastSearched }
    };

    [Test]
    public void ResultsAreOnABlockedDomain()
    {
        FilteredResults filtered = Apply(
            new BlockedDomains(["contentfarm.example"]),
            Result("https://blog.contentfarm.example/new", Now.AddHours(-1)),
            Result("https://example.com/new", Now.AddHours(-1)));

        ItShouldDropThem();
        ItShouldNotRecordThemAsSeen();

        void ItShouldDropThem() => filtered.Kept.Select(kept => kept.Url).ShouldBe(["https://example.com/new"]);

        void ItShouldNotRecordThemAsSeen() => filtered.Seen.ShouldBe(["https://example.com/new"]);
    }

    [Test]
    public void AResultWasSeenByAnEarlierSearch()
    {
        FilteredResults filtered = Apply(
            BlockedDomains.None,
            Result("http://www.example.com/seen/?utm_source=hn", Now.AddHours(-1)));

        ItShouldDropItHoweverItsUrlIsWritten();

        void ItShouldDropItHoweverItsUrlIsWritten() => filtered.ShouldSatisfyAllConditions(
            () => filtered.Kept.ShouldBeEmpty(),
            () => filtered.Seen.ShouldBeEmpty());
    }

    [Test]
    public void TwoSourcesFindTheSamePage()
    {
        FilteredResults filtered = Apply(
            BlockedDomains.None,
            Result("https://example.com/new", Now.AddHours(-1), "SearXNG"),
            Result("https://www.example.com/new#comments", Now.AddHours(-1), "Hacker News"));

        ItShouldKeepItOnce();

        void ItShouldKeepItOnce() => filtered.Kept.ShouldHaveSingleItem().Result.Source.ShouldBe("SearXNG");
    }

    [Test]
    public void AResultWasPublishedBeforeTheLastSearch()
    {
        FilteredResults filtered = Apply(
            BlockedDomains.None,
            Result("https://example.com/old", LastSearched.AddMinutes(-1)),
            Result("https://example.com/new", LastSearched.AddMinutes(1)));

        ItShouldDropIt();
        ItShouldStillRecordItAsSeen();

        void ItShouldDropIt() => filtered.Kept.Select(kept => kept.Url).ShouldBe(["https://example.com/new"]);

        void ItShouldStillRecordItAsSeen() => filtered.Seen.ShouldBe(["https://example.com/old", "https://example.com/new"]);
    }

    [Test]
    [Arguments(29, true)]
    [Arguments(31, false)]
    public void ATopicIsSearchedForTheFirstTime(int daysOld, bool kept)
    {
        ResearchTopic neverSearched = _topic with { LastSearchedAt = null, SeenUrls = null };

        FilteredResults filtered = ResultFilter.Apply(
            [(Query, Result("https://example.com/post", Now.AddDays(-daysOld)))],
            neverSearched,
            BlockedDomains.None,
            Now);

        ItShouldLookBackThirtyDays();

        void ItShouldLookBackThirtyDays() => (filtered.Kept.Count == 1).ShouldBe(kept);
    }

    [Test]
    public void AResultHasNoDate()
    {
        FilteredResults filtered = Apply(BlockedDomains.None, Result("https://example.com/undated", null));

        ItShouldKeepIt();

        void ItShouldKeepIt() => filtered.Kept.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            kept => kept.Query.ShouldBe(Query),
            kept => kept.Url.ShouldBe("https://example.com/undated"),
            kept => kept.Result.PublishedAt.ShouldBeNull());
    }

    [Test]
    public void ASourceReturnsSomethingThatIsNotAWebPage()
    {
        FilteredResults filtered = Apply(BlockedDomains.None, Result("javascript:alert(1)", Now));

        ItShouldIgnoreIt();

        void ItShouldIgnoreIt() => filtered.ShouldSatisfyAllConditions(
            () => filtered.Kept.ShouldBeEmpty(),
            () => filtered.Seen.ShouldBeEmpty());
    }

    [Test]
    public void ResultsAreDroppedForEveryReason()
    {
        FilteredResults filtered = Apply(
            new BlockedDomains(["contentfarm.example"]),
            Result("https://contentfarm.example/new", Now.AddHours(-1)),
            Result("javascript:alert(1)", Now),
            Result("https://example.com/seen", Now.AddHours(-1)),
            Result("https://example.com/new", Now.AddHours(-1)),
            Result("https://www.example.com/new", Now.AddHours(-1)),
            Result("https://example.com/old", LastSearched.AddDays(-1)));

        ItShouldCountEachReason();

        void ItShouldCountEachReason() => filtered.ShouldSatisfyAllConditions(
            () => filtered.Blocked.ShouldBe(2),
            () => filtered.AlreadySeen.ShouldBe(2),
            () => filtered.TooOld.ShouldBe(1),
            () => filtered.Kept.Count.ShouldBe(1));
    }

    private FilteredResults Apply(BlockedDomains blocked, params SearchResult[] results) =>
        ResultFilter.Apply(results.Select(result => (Query, result)), _topic, blocked, Now);

    private static SearchResult Result(string url, DateTimeOffset? publishedAt, string source = "SearXNG") =>
        new(url, "A result", null, source, publishedAt);
}
