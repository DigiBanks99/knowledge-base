using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Fetch;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Fetch;

public sealed class PendingResultFetchTests
{
    private const string Atlas = "projects/atlas";

    private static readonly DateTimeOffset Discovered = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly ResearchTopics _state = new();
    private readonly IPageFetcher _fetcher = Substitute.For<IPageFetcher>();
    private readonly PendingResultFetch _fetch;

    public PendingResultFetchTests()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Discovered);
        _fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<BlockedDomains>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<FittedContent, Error>(new FittedContent($"Content of {call.Arg<string>()}", false)));

        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _fetch = new PendingResultFetch(
            NullLogger<PendingResultFetch>.Instance,
            FakeResearchStore.Holding(_state),
            _fetcher,
            Substitute.For<IHostApplicationLifetime>(),
            timeProvider);
    }

    [Test]
    public async Task ABatchIsFetched()
    {
        _state.PendingBatches[Atlas] = Batch(Found("https://example.com/a"), Found("https://example.com/b"));

        Result<TopicFetchOutcome, Error> outcome = await _fetch.FetchAsync(Atlas, CancellationToken.None);

        ItShouldKeepEachPageWithItsResult();
        ItShouldReportWhatItFetched();

        void ItShouldKeepEachPageWithItsResult() => _state.PendingBatches[Atlas].Results.Select(result => result.Page).ShouldBe(
        [
            FetchedPage.Content(Now, "Content of https://example.com/a", truncated: false),
            FetchedPage.Content(Now, "Content of https://example.com/b", truncated: false)
        ]);

        void ItShouldReportWhatItFetched() => outcome.Value.ShouldBe(new TopicFetchOutcome(Atlas, 2, 0));
    }

    [Test]
    public async Task APageCannotBeFetched()
    {
        _state.PendingBatches[Atlas] = Batch(Found("https://example.com/a"), Found("https://example.com/private"));
        PageFetchRefusedError refused = new("https://example.com/private", "the site's robots.txt disallows it.");
        _fetcher.FetchAsync("https://example.com/private", Arg.Any<BlockedDomains>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<FittedContent, Error>(refused));

        Result<TopicFetchOutcome, Error> outcome = await _fetch.FetchAsync(Atlas, CancellationToken.None);

        ItShouldRecordWhyOnTheResult();
        ItShouldStillFetchTheOthers();
        ItShouldKeepTheBatchPending();

        void ItShouldRecordWhyOnTheResult() =>
            _state.PendingBatches[Atlas].Results[1].Page.ShouldBe(FetchedPage.Failed(Now, refused.Message));

        void ItShouldStillFetchTheOthers() => _state.PendingBatches[Atlas].Results[0].Page!.Markdown.ShouldNotBeNull();

        void ItShouldKeepTheBatchPending() => outcome.Value.ShouldBe(new TopicFetchOutcome(Atlas, 1, 1));
    }

    [Test]
    public async Task TheFetcherFailsUnexpectedly()
    {
        _state.PendingBatches[Atlas] = Batch(Found("https://example.com/hostile"), Found("https://example.com/b"));
        _fetcher.FetchAsync("https://example.com/hostile", Arg.Any<BlockedDomains>(), Arg.Any<CancellationToken>())
            .Returns<Result<FittedContent, Error>>(_ => throw new InvalidOperationException("The parser gave up."));

        Result<TopicFetchOutcome, Error> outcome = await _fetch.FetchAsync(Atlas, CancellationToken.None);

        ItShouldRecordItAsThatPagesFailure();
        ItShouldStillFetchTheOthers();

        // Otherwise the page would stay unfetched and fail the same way every cycle.
        void ItShouldRecordItAsThatPagesFailure() =>
            _state.PendingBatches[Atlas].Results[0].Page!.Error.ShouldBe("Could not fetch https://example.com/hostile: The parser gave up.");

        void ItShouldStillFetchTheOthers() => outcome.Value.ShouldBe(new TopicFetchOutcome(Atlas, 1, 1));
    }

    [Test]
    public async Task TheAuthorBlockedADomain()
    {
        _state.BlockedDomains = new BlockedDomains(["content-farm.example"]);
        _state.PendingBatches[Atlas] = Batch(Found("https://example.com/a"));

        await _fetch.FetchAsync(Atlas, CancellationToken.None);

        await ItShouldTellTheFetcherWhichDomainsAreBlocked();

        async Task ItShouldTellTheFetcherWhichDomainsAreBlocked() => await _fetcher.Received(1).FetchAsync(
            "https://example.com/a",
            _state.BlockedDomains,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AFetchWasInterruptedByARestart()
    {
        FetchedPage earlier = FetchedPage.Content(Now.AddHours(-1), "Fetched before the restart", truncated: false);
        _state.PendingBatches[Atlas] = Batch(Found("https://example.com/a") with { Page = earlier }, Found("https://example.com/b"));

        await _fetch.FetchAsync(Atlas, CancellationToken.None);

        await ItShouldOnlyFetchWhatIsMissing();
        ItShouldKeepWhatWasFetched();

        async Task ItShouldOnlyFetchWhatIsMissing()
        {
            await _fetcher.DidNotReceive().FetchAsync("https://example.com/a", Arg.Any<BlockedDomains>(), Arg.Any<CancellationToken>());
            await _fetcher.Received(1).FetchAsync("https://example.com/b", Arg.Any<BlockedDomains>(), Arg.Any<CancellationToken>());
        }

        void ItShouldKeepWhatWasFetched() => _state.PendingBatches[Atlas].Results[0].Page.ShouldBe(earlier);
    }

    [Test]
    public async Task TheTopicIsPrunedWhileItsPagesAreFetched()
    {
        _state.PendingBatches[Atlas] = Batch(Found("https://example.com/a"), Found("https://example.com/b"));
        _fetcher.FetchAsync("https://example.com/a", Arg.Any<BlockedDomains>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _state.PendingBatches.Remove(Atlas);
            return Result.Success<FittedContent, Error>(new FittedContent("Content", false));
        });

        Result<TopicFetchOutcome, Error> outcome = await _fetch.FetchAsync(Atlas, CancellationToken.None);

        await ItShouldStopFetching();
        ItShouldWriteNoBatch();

        async Task ItShouldStopFetching()
        {
            await _fetcher.DidNotReceive().FetchAsync("https://example.com/b", Arg.Any<BlockedDomains>(), Arg.Any<CancellationToken>());
            outcome.IsSuccess.ShouldBeTrue();
        }

        void ItShouldWriteNoBatch() => _state.PendingBatches.ShouldBeEmpty();
    }

    private static ResearchBatch Batch(params BatchResult[] results) => new(Atlas, Now.AddHours(-1), results);

    private static BatchResult Found(string url) =>
        new("atlas framework", url, new SearchResult(url, "A result", null, "SearXNG", null));
}
