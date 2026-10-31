using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class KeepTopicQueriesCommandHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    private readonly ResearchTopics _state = new();
    private readonly KeepTopicQueriesCommandHandler _handler;

    public KeepTopicQueriesCommandHandlerTests()
    {
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _handler = new KeepTopicQueriesCommandHandler(
            NullLogger<KeepTopicQueriesCommandHandler>.Instance,
            FakeResearchStore.Holding(_state),
            timeProvider);
    }

    [Test]
    public async Task ThePlebKeepsACuratedTopicsQueries()
    {
        _state.Topics["projects/atlas"] = ResearchTopic.New("projects/atlas", Discovered) with
        {
            Brief = "News about Atlas.",
            Queries = ["atlas release"],
            LastCuratedAt = Discovered
        };

        Result<ResearchTopic, Error> result = await _handler.Handle(new KeepTopicQueriesCommand("projects/atlas"), CancellationToken.None);

        ItShouldSucceed();
        ItShouldKeepTheBriefAndQueries();
        ItShouldRecordTheCuration();

        void ItShouldSucceed() => result.IsSuccess.ShouldBeTrue();

        void ItShouldKeepTheBriefAndQueries() => _state.Topics["projects/atlas"].ShouldSatisfyAllConditions(
            topic => topic.Brief.ShouldBe("News about Atlas."),
            topic => topic.Queries.ShouldBe(["atlas release"]));

        void ItShouldRecordTheCuration() => _state.Topics["projects/atlas"].LastCuratedAt.ShouldBe(Now);
    }

    [Test]
    public async Task ThePlebKeepsTheQueriesOfATopicThatHasNone()
    {
        _state.Topics["projects/atlas"] = ResearchTopic.New("projects/atlas", Discovered);

        Result<ResearchTopic, Error> result = await _handler.Handle(new KeepTopicQueriesCommand("projects/atlas"), CancellationToken.None);

        ItShouldBeRefused();
        ItShouldPointToSettingThem();
        ItShouldNotRecordACuration();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("NoQueriesToKeep");

        void ItShouldPointToSettingThem() => result.Error.Message.ShouldContain("Call `SetTopicQueries`");

        void ItShouldNotRecordACuration() => _state.Topics["projects/atlas"].LastCuratedAt.ShouldBeNull();
    }

    [Test]
    public async Task ThePlebKeepsTheQueriesOfATopicWaitingForReview()
    {
        _state.Topics["projects/atlas"] = ResearchTopic.New("projects/atlas", Discovered) with
        {
            Queries = ["atlas"],
            ReviewQuestion = "Which Atlas?"
        };

        Result<ResearchTopic, Error> result = await _handler.Handle(new KeepTopicQueriesCommand("projects/atlas"), CancellationToken.None);

        ItShouldBeRefused();
        ItShouldLeaveTheQuestionOpen();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<ValidationError>().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("ReviewPending"),
            error => error.Message.ShouldContain("Call `SetTopicQueries`"));

        void ItShouldLeaveTheQuestionOpen() => _state.Topics["projects/atlas"].NeedsReview.ShouldBeTrue();
    }

    [Test]
    public async Task ThePlebKeepsQueriesTheAuthorRejected()
    {
        _state.Topics["projects/atlas"] = ResearchTopic.New("projects/atlas", Discovered) with
        {
            Queries = ["atlas", "atlas news"],
            Guidance = [new AuthorGuidance(GuidanceKind.Rejection, null, Discovered, RejectedBrief: "Atlas.", RejectedQueries: ["Atlas News", "atlas"])]
        };

        Result<ResearchTopic, Error> result = await _handler.Handle(new KeepTopicQueriesCommand("projects/atlas"), CancellationToken.None);

        ItShouldBeRefused();
        ItShouldNotRecordACuration();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("RejectedQueries");

        void ItShouldNotRecordACuration() => _state.Topics["projects/atlas"].LastCuratedAt.ShouldBeNull();
    }
}
