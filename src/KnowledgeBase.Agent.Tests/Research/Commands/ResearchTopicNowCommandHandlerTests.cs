using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Fetch;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class ResearchTopicNowCommandHandlerTests
{
    private const string Atlas = "projects/atlas";

    private static readonly DateTimeOffset Discovered = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly ResearchTopics _state = new();
    private readonly ISearchSource _source = Substitute.For<ISearchSource>();
    private readonly IPageFetcher _fetcher = Substitute.For<IPageFetcher>();
    private readonly TaskCompletionSource<string> _fetched = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ResearchTopicNowCommandHandler _handler;

    public ResearchTopicNowCommandHandlerTests()
    {
        // Searched an hour ago, so not due for another 23.
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Discovered) with
        {
            Brief = "A UI framework.",
            Queries = ["atlas framework"],
            LastSearchedAt = Now.AddHours(-1)
        };
        _source.Name.Returns("SearXNG");
        _source.IsConfigured.Returns(true);
        _source.SearchAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<SearchResult>, Error>(
                [new SearchResult("https://example.com/atlas-2", "Atlas 2", null, "SearXNG", Now.AddMinutes(-10))]));

        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<BlockedDomains>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _fetched.TrySetResult(call.Arg<string>());
            return Result.Success<FittedContent, Error>(new FittedContent("Atlas 2 is out.", false));
        });

        WikiPaths wikiPaths = new(Options.Create(new WikiConfigurationSettings { RootDirectory = "/data/kb" }));
        IResearchStore store = FakeResearchStore.Holding(_state);
        _handler = new ResearchTopicNowCommandHandler(
            new TopicSearch(NullLogger<TopicSearch>.Instance, store, [_source], timeProvider),
            new PendingResultFetch(
                NullLogger<PendingResultFetch>.Instance,
                store,
                _fetcher,
                Substitute.For<IHostApplicationLifetime>(),
                timeProvider),
            wikiPaths);
    }

    [Test]
    public async Task TheAuthorResearchesATopicBeforeItIsDue()
    {
        Result<TopicSearchOutcome, Error> outcome = await _handler.Handle(new ResearchTopicNowCommand("projects--atlas"), CancellationToken.None);
        string fetched = await _fetched.Task.WaitAsync(TimeSpan.FromSeconds(10));

        ItShouldSearchItNow();
        ItShouldKeepItsNewResults();
        ItShouldFetchTheirPagesInTheBackground();

        void ItShouldSearchItNow() => outcome.Value.ShouldBe(new TopicSearchOutcome(Atlas, 1, 1));

        void ItShouldKeepItsNewResults() =>
            _state.PendingBatches[Atlas].Results.ShouldHaveSingleItem().Url.ShouldBe("https://example.com/atlas-2");

        void ItShouldFetchTheirPagesInTheBackground() => fetched.ShouldBe("https://example.com/atlas-2");
    }

    [Test]
    public async Task TheAuthorResearchesATopicWhoseResultsAreWaiting()
    {
        ResearchBatch waiting = new(Atlas, Now.AddHours(-1), []);
        _state.PendingBatches[Atlas] = waiting;

        Result<TopicSearchOutcome, Error> outcome = await _handler.Handle(new ResearchTopicNowCommand("projects--atlas"), CancellationToken.None);

        ItShouldBeRefused();
        ItShouldKeepTheWaitingResults();

        void ItShouldBeRefused() => outcome.Error.ShouldBeOfType<BatchInFlightError>().Topic.ShouldBe(Atlas);

        void ItShouldKeepTheWaitingResults() => _state.PendingBatches[Atlas].ShouldBeSameAs(waiting);
    }

    [Test]
    public async Task TheAuthorResearchesAnUnknownTopic()
    {
        Result<TopicSearchOutcome, Error> outcome = await _handler.Handle(new ResearchTopicNowCommand("projects--unknown"), CancellationToken.None);

        ItShouldBeNotFound();

        void ItShouldBeNotFound() => outcome.Error.ShouldBeOfType<TopicNotFoundError>().Topic.ShouldBe("projects/unknown");
    }

    [Test]
    [Arguments("..--..--notes")]
    [Arguments("people--jane-doe")]
    [Arguments("projects--atlas/../../../etc")]
    [Arguments("projects")]
    public async Task TheUIAsksForSomethingThatIsNotATopic(string slug)
    {
        Result<TopicSearchOutcome, Error> outcome = await _handler.Handle(new ResearchTopicNowCommand(slug), CancellationToken.None);

        ItShouldBeRefused();
        ItShouldSearchNothing();

        void ItShouldBeRefused() => outcome.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("InvalidTopic");

        void ItShouldSearchNothing() =>
            _source.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
