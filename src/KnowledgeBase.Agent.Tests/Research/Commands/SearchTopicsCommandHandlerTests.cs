using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class SearchTopicsCommandHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly ResearchTopics _state = new();
    private readonly ISearchSource _source = Substitute.For<ISearchSource>();
    private readonly SearchTopicsCommandHandler _handler;

    public SearchTopicsCommandHandlerTests()
    {
        _source.Name.Returns("SearXNG");
        _source.IsConfigured.Returns(true);
        _source.SearchAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<IReadOnlyList<SearchResult>, Error>(
                [new SearchResult($"https://example.com/{Uri.EscapeDataString(call.Arg<string>())}", "A result", null, "SearXNG", null)]));

        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        IResearchStore store = FakeResearchStore.Holding(_state);
        _handler = new SearchTopicsCommandHandler(
            NullLogger<SearchTopicsCommandHandler>.Instance,
            Options.Create(new ResearchSettings { TopicsPerCycle = 2 }),
            store,
            new TopicSearch(NullLogger<TopicSearch>.Instance, store, [_source], timeProvider),
            timeProvider);
    }

    [Test]
    public async Task ACycleRuns()
    {
        Add("projects/recent", lastSearched: Now.AddHours(-2));
        Add("projects/overdue", lastSearched: Now.AddDays(-3));
        Add("projects/due", lastSearched: Now.AddDays(-2));
        Add("projects/also-due", lastSearched: Now.AddHours(-25));
        Add("projects/in-flight", lastSearched: Now.AddDays(-4));
        _state.PendingBatches["projects/in-flight"] = new ResearchBatch("projects/in-flight", Now.AddDays(-4), []);

        Result<SearchCycle, Error> cycle = await _handler.Handle(new SearchTopicsCommand(), CancellationToken.None);

        ItShouldSearchTheMostOverdueUpToTheCap();
        ItShouldWriteTheirBatches();
        ItShouldLeaveTheRestDue();

        void ItShouldSearchTheMostOverdueUpToTheCap() =>
            cycle.Value.Searched.Select(outcome => outcome.Topic).ShouldBe(["projects/overdue", "projects/due"]);

        void ItShouldWriteTheirBatches() =>
            _state.PendingBatches.Keys.ShouldBe(["projects/in-flight", "projects/overdue", "projects/due"], ignoreOrder: true);

        void ItShouldLeaveTheRestDue() => _state.Topics["projects/also-due"].LastSearchedAt.ShouldBe(Now.AddHours(-25));
    }

    [Test]
    public async Task ATopicsSearchFails()
    {
        Add("projects/broken", lastSearched: Now.AddDays(-3), query: "broken");
        Add("projects/working", lastSearched: Now.AddDays(-2), query: "working");
        _source.SearchAsync("broken", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<SearchResult>, Error>(new SearchSourceError("SearXNG", "unreachable.")));

        Result<SearchCycle, Error> cycle = await _handler.Handle(new SearchTopicsCommand(), CancellationToken.None);

        ItShouldReportIt();
        ItShouldCarryOnWithTheRest();

        void ItShouldReportIt() => cycle.Value.Failed.ShouldBe(["projects/broken"]);

        void ItShouldCarryOnWithTheRest() => cycle.Value.Searched.ShouldHaveSingleItem().Topic.ShouldBe("projects/working");
    }

    [Test]
    public async Task NothingIsDue()
    {
        Add("projects/recent", lastSearched: Now.AddHours(-1));

        Result<SearchCycle, Error> cycle = await _handler.Handle(new SearchTopicsCommand(), CancellationToken.None);

        ItShouldSearchNothing();

        void ItShouldSearchNothing() => cycle.Value.ShouldSatisfyAllConditions(
            searched => searched.Searched.ShouldBeEmpty(),
            searched => searched.Failed.ShouldBeEmpty());
    }

    private void Add(string path, DateTimeOffset lastSearched, string query = "a query") =>
        _state.Topics[path] = ResearchTopic.New(path, Discovered) with { Brief = "A topic.", Queries = [query], LastSearchedAt = lastSearched };
}
