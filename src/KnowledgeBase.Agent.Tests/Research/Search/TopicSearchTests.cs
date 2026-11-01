using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Search;

public sealed class TopicSearchTests
{
    private const string Atlas = "projects/atlas";

    private static readonly DateTimeOffset Discovered = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastSearched = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly ResearchTopics _state = new();
    private readonly ISearchSource _searxng = Source("SearXNG");
    private readonly ISearchSource _hackerNews = Source("Hacker News");
    private readonly List<AgentRun> _recorded = [];
    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly TopicSearch _search;

    public TopicSearchTests()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Discovered) with
        {
            Brief = "A UI framework being migrated to.",
            Queries = ["atlas framework release", "atlas migration guide"],
            LastSearchedAt = LastSearched,
            SeenUrls = new Dictionary<string, DateTimeOffset> { ["https://example.com/seen"] = LastSearched }
        };

        _timeProvider.GetUtcNow().Returns(Now);
        ISearchRunLog searchRunLog = Substitute.For<ISearchRunLog>();
        searchRunLog.RecordAsync(Arg.Do<AgentRun>(_recorded.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _search = new TopicSearch(NullLogger<TopicSearch>.Instance, FakeResearchStore.Holding(_state), [_searxng, _hackerNews], searchRunLog, _timeProvider);
    }

    [Test]
    public async Task ATopicIsSearched()
    {
        Answer(_searxng, "atlas framework release", Found("https://example.com/atlas-2", Now.AddHours(-2)), Found("https://example.com/seen", Now.AddHours(-3)));
        Answer(_searxng, "atlas migration guide", Found("https://example.com/guide", null));
        Answer(_hackerNews, "atlas framework release", Found("https://www.example.com/atlas-2/", Now.AddHours(-1)));
        Answer(_hackerNews, "atlas migration guide", Found("https://example.com/old", LastSearched.AddDays(-1)));

        Result<TopicSearchOutcome, Error> outcome = await _search.SearchAsync(Atlas, SearchStart.Scheduled, CancellationToken.None);

        ItShouldSearchEveryQueryOnEverySourceSinceTheLastSearch();
        ItShouldReportWhatItFoundAndKept();
        ItShouldWriteTheKeptResultsAsThePendingBatch();
        ItShouldMarkTheTopicSearched();
        ItShouldRecordTheSearchAsRunningBeforeWritingItsBatch();
        ItShouldRecordTheSearchAsARunForTheTopic();
        ItShouldLinkTheBatchToItsRun();

        void ItShouldRecordTheSearchAsRunningBeforeWritingItsBatch() => _recorded.ShouldSatisfyAllConditions(
            recorded => recorded.Count.ShouldBe(2),
            recorded => recorded[0].Status.ShouldBe(AgentRunStatus.Running),
            recorded => recorded[0].Id.ShouldBe(recorded[1].Id));

        void ItShouldRecordTheSearchAsARunForTheTopic() => _recorded[^1].ShouldSatisfyAllConditions(
            run => run.Agent.ShouldBe(SearchRuns.Agent),
            run => run.Trigger.ShouldBe(new AgentRunTrigger(AgentRunTriggerKind.Search, "Scheduled")),
            run => run.Topic.ShouldBe(Atlas),
            run => run.Status.ShouldBe(AgentRunStatus.Succeeded),
            run => run.StartedAt.ShouldBe(Now),
            run => run.ToolCalls.Count.ShouldBe(4),
            run => run.Summary.ShouldBe("Found 5 results: 0 blocked, 2 already seen, 1 too old, 2 kept. Wrote a batch of 2 for triage."));

        void ItShouldLinkTheBatchToItsRun() => _state.PendingBatches[Atlas].SearchRunId.ShouldBe(_recorded[^1].Id);

        void ItShouldSearchEveryQueryOnEverySourceSinceTheLastSearch()
        {
            _searxng.Received(2).SearchAsync(Arg.Any<string>(), LastSearched, Arg.Any<CancellationToken>());
            _hackerNews.Received(2).SearchAsync(Arg.Any<string>(), LastSearched, Arg.Any<CancellationToken>());
        }

        void ItShouldReportWhatItFoundAndKept() => outcome.Value.ShouldBe(new TopicSearchOutcome(Atlas, 5, 2));

        void ItShouldWriteTheKeptResultsAsThePendingBatch() => _state.PendingBatches[Atlas].ShouldSatisfyAllConditions(
            batch => batch.SearchedAt.ShouldBe(Now),
            batch => batch.Results.Select(result => (result.Query, result.Url)).ShouldBe(
            [
                ("atlas framework release", "https://example.com/atlas-2"),
                ("atlas migration guide", "https://example.com/guide")
            ]));

        void ItShouldMarkTheTopicSearched() => _state.Topics[Atlas].ShouldSatisfyAllConditions(
            topic => topic.LastSearchedAt.ShouldBe(Now),
            topic => topic.SeenUrls!.Keys.ShouldBe(
                ["https://example.com/seen", "https://example.com/atlas-2", "https://example.com/guide", "https://example.com/old"],
                ignoreOrder: true),
            topic => topic.SeenUrls!["https://example.com/seen"].ShouldBe(LastSearched));
    }

    [Test]
    public async Task ATopicIsSearchedForTheFirstTime()
    {
        _state.Topics[Atlas] = _state.Topics[Atlas] with { LastSearchedAt = null, SeenUrls = null };

        await _search.SearchAsync(Atlas, SearchStart.Scheduled, CancellationToken.None);

        ItShouldAskForTheLastThirtyDays();

        void ItShouldAskForTheLastThirtyDays() =>
            _searxng.Received(2).SearchAsync(Arg.Any<string>(), Now.AddDays(-30), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NothingNewIsFound()
    {
        Answer(_searxng, "atlas framework release", Found("https://example.com/seen", Now.AddHours(-3)));

        Result<TopicSearchOutcome, Error> outcome = await _search.SearchAsync(Atlas, SearchStart.Scheduled, CancellationToken.None);

        ItShouldKeepNothing();
        ItShouldWriteNoBatch();
        ItShouldStillMarkTheTopicSearched();
        ItShouldStillRecordTheSearch();

        void ItShouldStillRecordTheSearch() => _recorded.ShouldHaveSingleItem().Summary.ShouldBe(
            "Found 1 result: 0 blocked, 1 already seen, 0 too old, 0 kept. Kept nothing, so no batch was written.");

        void ItShouldKeepNothing() => outcome.Value.Kept.ShouldBe(0);

        void ItShouldWriteNoBatch() => _state.PendingBatches.ShouldBeEmpty();

        void ItShouldStillMarkTheTopicSearched() => _state.Topics[Atlas].LastSearchedAt.ShouldBe(Now);
    }

    [Test]
    public async Task OneSourceFails()
    {
        Fail(_searxng);
        Answer(_hackerNews, "atlas framework release", Found("https://example.com/atlas-2", Now.AddHours(-1)));

        Result<TopicSearchOutcome, Error> outcome = await _search.SearchAsync(Atlas, SearchStart.Scheduled, CancellationToken.None);

        ItShouldUseTheOther();
        ItShouldRecordWhatItSaw();
        ItShouldKeepTheCutoffForTheFailedSource();

        void ItShouldUseTheOther() => outcome.Value.Kept.ShouldBe(1);

        void ItShouldRecordWhatItSaw() => _state.Topics[Atlas].HasSeen("https://example.com/atlas-2").ShouldBeTrue();

        void ItShouldKeepTheCutoffForTheFailedSource() =>
            _state.Topics[Atlas].LastSearchedAt.ShouldBe(LastSearched);
    }

    [Test]
    public async Task ATopicIsAlreadyBeingSearched()
    {
        TaskCompletionSource<Result<IReadOnlyList<SearchResult>, Error>> slowAnswer = new();
        _searxng.SearchAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(slowAnswer.Task);

        Task<Result<TopicSearchOutcome, Error>> first = _search.SearchAsync(Atlas, SearchStart.Scheduled, CancellationToken.None);
        Result<TopicSearchOutcome, Error> second = await _search.SearchAsync(Atlas, SearchStart.Scheduled, CancellationToken.None);
        slowAnswer.SetResult(Result.Success<IReadOnlyList<SearchResult>, Error>([]));
        Result<TopicSearchOutcome, Error> firstOutcome = await first;

        ItShouldRefuseTheSecondSearch();
        ItShouldLetTheFirstFinish();

        void ItShouldRefuseTheSecondSearch() => second.Error.ShouldBeOfType<TopicBeingSearchedError>().Topic.ShouldBe(Atlas);

        void ItShouldLetTheFirstFinish() => firstOutcome.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async Task EverySourceFails()
    {
        Fail(_searxng);
        Fail(_hackerNews);

        Result<TopicSearchOutcome, Error> outcome = await _search.SearchAsync(Atlas, SearchStart.Scheduled, CancellationToken.None);

        ItShouldFail();
        ItShouldLeaveTheTopicDue();
        ItShouldRecordTheSearchAsFailed();

        void ItShouldRecordTheSearchAsFailed() => _recorded.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            run => run.Status.ShouldBe(AgentRunStatus.Failed),
            run => run.Error.ShouldBe(outcome.Error.Message),
            run => run.ToolCalls.ShouldAllBe(call => !call.Succeeded));

        void ItShouldFail() => outcome.Error.ShouldBeOfType<AggregateError>().Errors.Count().ShouldBe(4);

        void ItShouldLeaveTheTopicDue() => _state.Topics[Atlas].LastSearchedAt.ShouldBe(LastSearched);
    }

    [Test]
    public async Task ASourceIsNotConfigured()
    {
        _searxng.IsConfigured.Returns(false);

        await _search.SearchAsync(Atlas, SearchStart.Scheduled, CancellationToken.None);

        ItShouldNotSearchIt();

        void ItShouldNotSearchIt() =>
            _searxng.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AResultIsOnABlockedDomain()
    {
        _state.BlockedDomains = new BlockedDomains(["contentfarm.example"]);
        Answer(_searxng, "atlas framework release", Found("https://contentfarm.example/atlas", Now.AddHours(-1)));

        Result<TopicSearchOutcome, Error> outcome = await _search.SearchAsync(Atlas, SearchStart.Scheduled, CancellationToken.None);

        ItShouldNotKeepIt();

        void ItShouldNotKeepIt() => outcome.Value.Kept.ShouldBe(0);
    }

    [Test]
    public async Task TheTopicCannotBeSearched()
    {
        _state.Topics["projects/muted"] = _state.Topics[Atlas] with { Path = "projects/muted", Muted = true };
        _state.Topics["projects/uncurated"] = ResearchTopic.New("projects/uncurated", Discovered);
        _state.PendingBatches[Atlas] = new ResearchBatch(Atlas, LastSearched, []);

        Result<TopicSearchOutcome, Error> muted = await _search.SearchAsync("projects/muted", SearchStart.Scheduled, CancellationToken.None);
        Result<TopicSearchOutcome, Error> uncurated = await _search.SearchAsync("projects/uncurated", SearchStart.Scheduled, CancellationToken.None);
        Result<TopicSearchOutcome, Error> inFlight = await _search.SearchAsync(Atlas, SearchStart.Scheduled, CancellationToken.None);
        Result<TopicSearchOutcome, Error> unknown = await _search.SearchAsync("projects/unknown", SearchStart.Scheduled, CancellationToken.None);

        ItShouldRefuseEachWithItsReason();
        ItShouldSearchNothing();
        ItShouldRecordNothing();

        void ItShouldRefuseEachWithItsReason() => this.ShouldSatisfyAllConditions(
            () => muted.Error.ShouldBeOfType<TopicMutedError>(),
            () => uncurated.Error.ShouldBeOfType<TopicNotCuratedError>(),
            () => inFlight.Error.ShouldBeOfType<BatchInFlightError>(),
            () => unknown.Error.ShouldBeOfType<TopicNotFoundError>());

        void ItShouldSearchNothing() =>
            _searxng.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());

        void ItShouldRecordNothing() => _recorded.ShouldBeEmpty();
    }

    [Test]
    public async Task TheAuthorAsksForResearchNow()
    {
        Answer(_searxng, "atlas framework release", Found("https://example.com/atlas-2", Now.AddHours(-2)));

        await _search.SearchAsync(Atlas, SearchStart.ResearchNow, CancellationToken.None);

        ItShouldRecordHowTheSearchWasStarted();

        void ItShouldRecordHowTheSearchWasStarted() =>
            _recorded[^1].Trigger.ShouldBe(new AgentRunTrigger(AgentRunTriggerKind.Search, "ResearchNow"));
    }

    [Test]
    public async Task TheSearchCannotBeRecorded()
    {
        IAgentRunStore runStore = Substitute.For<IAgentRunStore>();
        runStore.SaveAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()).Returns(_ => throw new IOException("The disk is full."));
        TopicSearch search = new(
            NullLogger<TopicSearch>.Instance,
            FakeResearchStore.Holding(_state),
            [_searxng],
            new SearchRunLog(NullLogger<SearchRunLog>.Instance, runStore),
            _timeProvider);
        Answer(_searxng, "atlas framework release", Found("https://example.com/atlas-2", Now.AddHours(-2)));

        Result<TopicSearchOutcome, Error> outcome = await search.SearchAsync(Atlas, SearchStart.Scheduled, CancellationToken.None);

        ItShouldStillSearch();

        void ItShouldStillSearch() => outcome.ShouldSatisfyAllConditions(
            () => outcome.Value.Kept.ShouldBe(1),
            () => _state.PendingBatches.Keys.ShouldBe([Atlas]));
    }

    private static ISearchSource Source(string name)
    {
        ISearchSource source = Substitute.For<ISearchSource>();
        source.Name.Returns(name);
        source.IsConfigured.Returns(true);
        source.SearchAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<SearchResult>, Error>([]));
        return source;
    }

    // The source's name is read before the call being configured, as NSubstitute would take it for that call's return.
    private static void Answer(ISearchSource source, string query, params SearchResult[] results)
    {
        SearchResult[] found = [.. results.Select(result => result with { Source = source.Name })];
        source.SearchAsync(query, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<SearchResult>, Error>(found));
    }

    private static void Fail(ISearchSource source)
    {
        SearchSourceError error = new(source.Name, "unreachable.");
        source.SearchAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<SearchResult>, Error>(error));
    }

    private static SearchResult Found(string url, DateTimeOffset? publishedAt) => new(url, "A result", null, "", publishedAt);
}
