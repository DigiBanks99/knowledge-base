using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class RecurateTopicCommandHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Atlas = "projects/atlas";

    private readonly ResearchTopics _state = new();
    private readonly RecurateTopicCommandHandler _handler;

    public RecurateTopicCommandHandlerTests()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Discovered) with { Queries = ["atlas release"], Pinned = true };
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _handler = new RecurateTopicCommandHandler(
            NullLogger<RecurateTopicCommandHandler>.Instance,
            FakeResearchStore.Holding(_state),
            new WikiPaths(Options.Create(new WikiConfigurationSettings { RootDirectory = "/data/kb" })),
            timeProvider);
    }

    [Test]
    public async Task TheAuthorRecuratesAnEditedTopic()
    {
        await _handler.Handle(new RecurateTopicCommand("projects--atlas"), CancellationToken.None);

        ItShouldUnpinIt();
        ItShouldQueueACurationForTheAuthor();
        ItShouldKeepItsQueriesUntilThePlebReplacesThem();

        void ItShouldUnpinIt() => _state.Topics[Atlas].Pinned.ShouldBeFalse();

        void ItShouldQueueACurationForTheAuthor() => _state.CurationRequests[Atlas].ShouldSatisfyAllConditions(
            request => request.ByAuthor.ShouldBeTrue(),
            request => request.ChangedPaths.ShouldBeEmpty());

        void ItShouldKeepItsQueriesUntilThePlebReplacesThem() => _state.Topics[Atlas].Queries.ShouldBe(["atlas release"]);
    }

    [Test]
    public async Task TheAuthorRecuratesATopicWhosePagesChanged()
    {
        _state.CurationRequests[Atlas] = new CurationRequest(Atlas, ["projects/atlas/roadmap.md"], Discovered);

        await _handler.Handle(new RecurateTopicCommand("projects--atlas"), CancellationToken.None);

        ItShouldMergeIntoTheWaitingRequest();

        void ItShouldMergeIntoTheWaitingRequest() => _state.CurationRequests[Atlas].ShouldSatisfyAllConditions(
            request => request.ByAuthor.ShouldBeTrue(),
            request => request.ChangedPaths.ShouldBe(["projects/atlas/roadmap.md"]));
    }

    [Test]
    public async Task TheAuthorRecuratesAnUnknownTopic()
    {
        Result<ResearchTopic, Error> result = await _handler.Handle(new RecurateTopicCommand("projects--unknown"), CancellationToken.None);

        ItShouldBeNotFound();
        ItShouldQueueNothing();

        void ItShouldBeNotFound() => result.Error.ShouldBeOfType<TopicNotFoundError>();

        void ItShouldQueueNothing() => _state.CurationRequests.ShouldBeEmpty();
    }
}
