using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class EditTopicCurationCommandHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Okf = "learning/okf.md";

    private readonly ResearchTopics _state = new();
    private readonly EditTopicCurationCommandHandler _handler;

    public EditTopicCurationCommandHandlerTests()
    {
        _state.Topics[Okf] = ResearchTopic.New(Okf, Discovered) with
        {
            Brief = "News about OKF.",
            Queries = ["okf"],
            ReviewQuestion = "Which OKF is meant?",
            ApprovedAt = Discovered
        };
        _state.CurationRequests[Okf] = new CurationRequest(Okf, ["learning/okf.md"], Discovered);
        _state.FailedCurations[Okf] = new FailedCuration(new CurationRequest(Okf, [], Discovered), "No verdict.", Discovered);
        _state.DenyList = new DenyList([new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied)]);
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _handler = new EditTopicCurationCommandHandler(
            NullLogger<EditTopicCurationCommandHandler>.Instance,
            FakeResearchStore.Holding(_state),
            new WikiPaths(Options.Create(new WikiConfigurationSettings { RootDirectory = "/data/kb" })),
            timeProvider);
    }

    [Test]
    public async Task TheAuthorEditsACuration()
    {
        Result<ResearchTopic, Error> result = await _handler.Handle(
            new EditTopicCurationCommand("learning--okf.md", " News about the Open Knowledge Format for data packages. ", ["open knowledge format", " okf data package "]),
            CancellationToken.None);

        ItShouldSucceed();
        ItShouldStoreTheTrimmedBriefAndQueries();
        ItShouldPinTheTopic();
        ItShouldAnswerTheOpenQuestion();
        ItShouldDropTheWaitingAndFailedCurations();

        void ItShouldSucceed() => result.IsSuccess.ShouldBeTrue();

        void ItShouldStoreTheTrimmedBriefAndQueries() => _state.Topics[Okf].ShouldSatisfyAllConditions(
            topic => topic.Brief.ShouldBe("News about the Open Knowledge Format for data packages."),
            topic => topic.Queries.ShouldBe(["open knowledge format", "okf data package"]),
            topic => topic.LastCuratedAt.ShouldBe(Now));

        void ItShouldPinTheTopic() => _state.Topics[Okf].Pinned.ShouldBeTrue();

        void ItShouldAnswerTheOpenQuestion() => _state.Topics[Okf].ShouldSatisfyAllConditions(
            topic => topic.NeedsReview.ShouldBeFalse(),
            topic => topic.ApprovedAt.ShouldBeNull());

        void ItShouldDropTheWaitingAndFailedCurations()
        {
            _state.CurationRequests.ShouldBeEmpty();
            _state.FailedCurations.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task TheAuthorPutsADeniedNameInAQuery()
    {
        Result<ResearchTopic, Error> result = await _handler.Handle(
            new EditTopicCurationCommand("learning--okf.md", "News about the Open Knowledge Format.", ["jane doe okf"]),
            CancellationToken.None);

        ItShouldBeRefusedAsThePlebWouldBe();
        ItShouldChangeNothing();

        void ItShouldBeRefusedAsThePlebWouldBe() => result.Error.Code.ShouldBe("DeniedNameInQuery");

        void ItShouldChangeNothing()
        {
            _state.Topics[Okf].ShouldSatisfyAllConditions(
                topic => topic.Queries.ShouldBe(["okf"]),
                topic => topic.Pinned.ShouldBeFalse());
            _state.CurationRequests.Keys.ShouldBe([Okf]);
        }
    }
}
