using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class ClarifyTopicCommandHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Okf = "learning/okf.md";

    private readonly ResearchTopics _state = new();
    private readonly ClarifyTopicCommandHandler _handler;

    public ClarifyTopicCommandHandlerTests()
    {
        _state.Topics[Okf] = ResearchTopic.New(Okf, Discovered) with
        {
            Queries = ["okf"],
            ReviewQuestion = "Which OKF is meant?",
            Pinned = true
        };
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _handler = new ClarifyTopicCommandHandler(
            NullLogger<ClarifyTopicCommandHandler>.Instance,
            Options.Create(new ResearchSettings { GuidanceLimit = 2 }),
            FakeResearchStore.Holding(_state),
            new WikiPaths(Options.Create(new WikiConfigurationSettings { RootDirectory = "/data/kb" })),
            timeProvider);
    }

    [Test]
    public async Task TheAuthorAnswersThePlebsQuestion()
    {
        Result<ResearchTopic, Error> result = await _handler.Handle(
            new ClarifyTopicCommand("learning--okf.md", "  The Open Knowledge Foundation's data format.  "),
            CancellationToken.None);

        ItShouldSucceed();
        ItShouldKeepTheAnswerWithItsQuestion();
        ItShouldQueueACurationForTheAuthor();
        ItShouldKeepTheTopicUnsearchedUntilThePlebSetsQueries();
        ItShouldUnpinTheTopic();

        void ItShouldSucceed() => result.IsSuccess.ShouldBeTrue();

        void ItShouldKeepTheAnswerWithItsQuestion() => _state.Topics[Okf].Guidance.ShouldNotBeNull().ShouldHaveSingleItem()
            .ShouldBe(new AuthorGuidance(GuidanceKind.Clarification, "The Open Knowledge Foundation's data format.", Now, "Which OKF is meant?"));

        void ItShouldQueueACurationForTheAuthor() => _state.CurationRequests[Okf].ShouldSatisfyAllConditions(
            request => request.ByAuthor.ShouldBeTrue(),
            request => request.RequestedAt.ShouldBe(Now));

        void ItShouldKeepTheTopicUnsearchedUntilThePlebSetsQueries() => _state.Topics[Okf].NeedsReview.ShouldBeTrue();

        void ItShouldUnpinTheTopic() => _state.Topics[Okf].Pinned.ShouldBeFalse();
    }

    [Test]
    public async Task TheAuthorGivesAnEmptyAnswer()
    {
        Result<ResearchTopic, Error> result = await _handler.Handle(new ClarifyTopicCommand("learning--okf.md", " "), CancellationToken.None);

        ItShouldBeRefused();
        ItShouldQueueNothing();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("EmptyText");

        void ItShouldQueueNothing() => _state.CurationRequests.ShouldBeEmpty();
    }

    [Test]
    public async Task TheAuthorClarifiesATopicWithoutAQuestion()
    {
        _state.Topics[Okf] = _state.Topics[Okf] with { ReviewQuestion = null };

        Result<ResearchTopic, Error> result = await _handler.Handle(new ClarifyTopicCommand("learning--okf.md", "The data format."), CancellationToken.None);

        ItShouldBeRefused();
        ItShouldQueueNothing();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<TopicNotInReviewError>();

        void ItShouldQueueNothing() => _state.CurationRequests.ShouldBeEmpty();
    }
}
