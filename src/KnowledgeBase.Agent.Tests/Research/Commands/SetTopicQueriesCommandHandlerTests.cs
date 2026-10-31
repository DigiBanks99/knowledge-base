using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class SetTopicQueriesCommandHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    private readonly ResearchTopics _state = new();
    private readonly SetTopicQueriesCommandHandler _handler;

    public SetTopicQueriesCommandHandlerTests()
    {
        _state.Topics["projects/atlas"] = ResearchTopic.New("projects/atlas", Discovered) with { Muted = true };
        _state.DenyList = new DenyList([new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied)]);
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _handler = new SetTopicQueriesCommandHandler(
            NullLogger<SetTopicQueriesCommandHandler>.Instance,
            FakeResearchStore.Holding(_state),
            timeProvider);
    }

    [Test]
    public async Task ThePlebGivesATopicItsQueries()
    {
        Result<ResearchTopic, Error> result = await _handler.Handle(
            new SetTopicQueriesCommand("projects/atlas", "News about the Atlas framework.", ["atlas release"]),
            CancellationToken.None);

        ItShouldSucceed();
        ItShouldStoreTheBriefAndQueries();
        ItShouldRecordWhenItWasCurated();
        ItShouldKeepTheAuthorsMute();

        void ItShouldSucceed() => result.IsSuccess.ShouldBeTrue();

        void ItShouldStoreTheBriefAndQueries() => _state.Topics["projects/atlas"].ShouldSatisfyAllConditions(
            topic => topic.Brief.ShouldBe("News about the Atlas framework."),
            topic => topic.Queries.ShouldBe(["atlas release"]));

        void ItShouldRecordWhenItWasCurated() => _state.Topics["projects/atlas"].LastCuratedAt.ShouldBe(Now);

        void ItShouldKeepTheAuthorsMute() => _state.Topics["projects/atlas"].Muted.ShouldBeTrue();
    }

    [Test]
    public async Task ThePlebCuratesATopicAfterTheAuthorsAnswer()
    {
        _state.Topics["projects/atlas"] = _state.Topics["projects/atlas"] with
        {
            Queries = ["atlas"],
            ReviewQuestion = "Which Atlas?",
            ApprovedAt = Discovered
        };

        await _handler.Handle(
            new SetTopicQueriesCommand("projects/atlas", "News about the Atlas robotics framework.", ["atlas robotics release"]),
            CancellationToken.None);

        ItShouldCloseTheQuestion();
        ItShouldNeedApprovingAgain();

        void ItShouldCloseTheQuestion() => _state.Topics["projects/atlas"].NeedsReview.ShouldBeFalse();

        void ItShouldNeedApprovingAgain() => _state.Topics["projects/atlas"].ApprovedAt.ShouldBeNull();
    }

    [Test]
    public async Task ThePlebCuratesATopicTheAuthorEdited()
    {
        _state.Topics["projects/atlas"] = _state.Topics["projects/atlas"] with { Brief = "The author's brief.", Queries = ["authors query"], Pinned = true };

        Result<ResearchTopic, Error> result = await _handler.Handle(
            new SetTopicQueriesCommand("projects/atlas", "News about Atlas.", ["atlas release"]),
            CancellationToken.None);

        ItShouldBeRefused();
        ItShouldKeepTheAuthorsEdit();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<TopicPinnedError>();

        void ItShouldKeepTheAuthorsEdit() => _state.Topics["projects/atlas"].Queries.ShouldBe(["authors query"]);
    }

    [Test]
    public async Task ThePlebProposesQueriesTheAuthorRejected()
    {
        _state.Topics["projects/atlas"] = _state.Topics["projects/atlas"] with
        {
            Queries = ["atlas"],
            Guidance = [new AuthorGuidance(GuidanceKind.Rejection, "Too vague.", Discovered, RejectedBrief: "Atlas.", RejectedQueries: ["atlas"])]
        };

        Result<ResearchTopic, Error> result = await _handler.Handle(
            new SetTopicQueriesCommand("projects/atlas", "A different brief about Atlas.", ["Atlas"]),
            CancellationToken.None);

        ItShouldBeRefused();
        ItShouldStoreNothing();

        void ItShouldBeRefused() => result.Error.Code.ShouldBe("RejectedQueries");

        void ItShouldStoreNothing() => _state.Topics["projects/atlas"].LastCuratedAt.ShouldBeNull();
    }

    [Test]
    public async Task ThePlebPutsADeniedNameInAQuery()
    {
        Result<ResearchTopic, Error> result = await _handler.Handle(
            new SetTopicQueriesCommand("projects/atlas", "News about the Atlas framework.", ["jane doe atlas"]),
            CancellationToken.None);

        ItShouldBeRefused();
        ItShouldStoreNothing();

        void ItShouldBeRefused() => result.Error.Code.ShouldBe("DeniedNameInQuery");

        void ItShouldStoreNothing() => _state.Topics["projects/atlas"].ShouldSatisfyAllConditions(
            topic => topic.Queries.ShouldBeEmpty(),
            topic => topic.LastCuratedAt.ShouldBeNull());
    }

    [Test]
    public async Task ThePlebCuratesATopicThatWasPruned()
    {
        Result<ResearchTopic, Error> result = await _handler.Handle(
            new SetTopicQueriesCommand("projects/gone", "News about something.", ["something release"]),
            CancellationToken.None);

        ItShouldBeNotFound();
        ItShouldNotCreateIt();

        void ItShouldBeNotFound() => result.Error.ShouldBeOfType<TopicNotFoundError>().Topic.ShouldBe("projects/gone");

        void ItShouldNotCreateIt() => _state.Topics.Keys.ShouldBe(["projects/atlas"]);
    }
}
