using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Fetch;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class FetchPendingResultsCommandHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly ResearchTopics _state = new();
    private readonly IResearchStore _store;
    private readonly IPageFetcher _fetcher = Substitute.For<IPageFetcher>();
    private readonly FetchPendingResultsCommandHandler _handler;

    public FetchPendingResultsCommandHandlerTests()
    {
        _fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<BlockedDomains>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<FittedContent, Error>(new FittedContent("Content", false)));

        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _store = FakeResearchStore.Holding(_state);
        _handler = new FetchPendingResultsCommandHandler(
            NullLogger<FetchPendingResultsCommandHandler>.Instance,
            _store,
            new PendingResultFetch(
                NullLogger<PendingResultFetch>.Instance,
                _store,
                _fetcher,
                Substitute.For<ISearchRunLog>(),
                Substitute.For<IHostApplicationLifetime>(),
                timeProvider));
    }

    [Test]
    public async Task TwoTopicsHavePendingResults()
    {
        Add("projects/atlas", "https://example.com/atlas", "https://example.com/broken");
        Add("learning/rust", "https://example.com/rust");
        _fetcher.FetchAsync("https://example.com/broken", Arg.Any<BlockedDomains>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<FittedContent, Error>(new PageFetchFailedError("https://example.com/broken", "it answered 500.")));

        Result<FetchCycle, Error> cycle = await _handler.Handle(new FetchPendingResultsCommand(), CancellationToken.None);

        ItShouldFetchEveryTopicsPages();
        ItShouldReportEachTopic();

        void ItShouldFetchEveryTopicsPages() =>
            _state.PendingBatches.Values.SelectMany(batch => batch.Results).ShouldAllBe(result => result.Page != null);

        void ItShouldReportEachTopic() => cycle.Value.ShouldSatisfyAllConditions(
            value => value.Fetched.ShouldBe([new TopicFetchOutcome("learning/rust", 1, 0), new TopicFetchOutcome("projects/atlas", 1, 1)]),
            value => value.Failed.ShouldBeEmpty());
    }

    [Test]
    public async Task ATopicsBatchCannotBeRead()
    {
        Add("projects/atlas", "https://example.com/atlas");
        Add("learning/rust", "https://example.com/rust");
        _store.ReadPendingBatchAsync("projects/atlas", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Maybe<ResearchBatch>, Error>(new ResearchStoreError("projects--atlas.json", "corrupt")));

        Result<FetchCycle, Error> cycle = await _handler.Handle(new FetchPendingResultsCommand(), CancellationToken.None);

        ItShouldCarryOnWithTheOthers();

        void ItShouldCarryOnWithTheOthers() => cycle.Value.ShouldSatisfyAllConditions(
            value => value.Fetched.ShouldBe([new TopicFetchOutcome("learning/rust", 1, 0)]),
            value => value.Failed.ShouldBe(["projects/atlas"]));
    }

    private void Add(string topic, params string[] urls)
    {
        _state.Topics[topic] = ResearchTopic.New(topic, Discovered);
        _state.PendingBatches[topic] = new ResearchBatch(
            topic,
            Now.AddHours(-1),
            [.. urls.Select(url => new BatchResult("a query", url, new SearchResult(url, "A result", null, "SearXNG", null)))]);
    }
}
