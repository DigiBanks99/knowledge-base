using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Queries;

namespace KnowledgeBase.Agent.Tests.Research.Queries;

public sealed class ListTopicsQueryHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);

    private readonly ResearchTopics _state = new();
    private readonly ListTopicsQueryHandler _handler;

    public ListTopicsQueryHandlerTests() => _handler = new ListTopicsQueryHandler(FakeResearchStore.Holding(_state));

    [Test]
    public async Task TopicsAreInEveryState()
    {
        _state.Topics["projects/zeta"] = ResearchTopic.New("projects/zeta", Discovered);
        _state.Topics["learning/rust.md"] = ResearchTopic.New("learning/rust.md", Discovered);
        _state.Topics["plans/q4.md"] = ResearchTopic.New("plans/q4.md", Discovered) with { Muted = true };
        _state.CurationRequests["learning/rust.md"] = new CurationRequest("learning/rust.md", [], Discovered);
        _state.CurationRequests["plans/q4.md"] = new CurationRequest("plans/q4.md", [], Discovered);

        Result<IReadOnlyList<TopicSummary>, Error> result = await _handler.Handle(new ListTopicsQuery(), CancellationToken.None);

        ItShouldListThemByPath();
        ItShouldDeriveEachState();
        ItShouldGiveEachItsSlug();

        void ItShouldListThemByPath() =>
            result.Value.Select(topic => topic.Path).ShouldBe(["learning/rust.md", "plans/q4.md", "projects/zeta"]);

        void ItShouldDeriveEachState() => result.Value.Select(topic => topic.State)
            .ShouldBe([TopicState.AwaitingCuration, TopicState.Muted, TopicState.Idle]);

        void ItShouldGiveEachItsSlug() =>
            result.Value.Select(topic => topic.Slug).ShouldBe(["learning--rust.md", "plans--q4.md", "projects--zeta"]);
    }

    [Test]
    public async Task ThePlebAskedTheAuthorAboutATopic()
    {
        AuthorGuidance answer = new(GuidanceKind.Clarification, "The robotics framework.", Discovered, "Which Atlas?");
        _state.Topics["projects/asked"] = ResearchTopic.New("projects/asked", Discovered) with { ReviewQuestion = "Which Atlas?" };
        _state.Topics["projects/answered"] = ResearchTopic.New("projects/answered", Discovered) with
        {
            ReviewQuestion = "Which Atlas?",
            Guidance = [answer],
            Pinned = true
        };
        _state.CurationRequests["projects/answered"] = new CurationRequest("projects/answered", [], Discovered, ByAuthor: true);

        Result<IReadOnlyList<TopicSummary>, Error> result = await _handler.Handle(new ListTopicsQuery(), CancellationToken.None);

        ItShouldShowAnUnansweredQuestionAsNeedingReview();
        ItShouldShowAnAnsweredOneAsWaitingForTheCuration();
        ItShouldShowTheQuestionAndGuidance();

        void ItShouldShowAnUnansweredQuestionAsNeedingReview() => result.Value[1].State.ShouldBe(TopicState.NeedsReview);

        void ItShouldShowAnAnsweredOneAsWaitingForTheCuration() => result.Value[0].State.ShouldBe(TopicState.AwaitingCuration);

        void ItShouldShowTheQuestionAndGuidance() => result.Value[0].ShouldSatisfyAllConditions(
            topic => topic.ReviewQuestion.ShouldBe("Which Atlas?"),
            topic => topic.Guidance.ShouldBe([answer]),
            topic => topic.Pinned.ShouldBeTrue());
    }

    [Test]
    public async Task TheCurationAfterTheAuthorsAnswerFails()
    {
        _state.Topics["learning/okf.md"] = ResearchTopic.New("learning/okf.md", Discovered) with { ReviewQuestion = "Which OKF?" };
        _state.FailedCurations["learning/okf.md"] = new FailedCuration(new CurationRequest("learning/okf.md", [], Discovered, ByAuthor: true), "No verdict.", Discovered);

        Result<IReadOnlyList<TopicSummary>, Error> result = await _handler.Handle(new ListTopicsQuery(), CancellationToken.None);

        ItShouldShowTheFailureRatherThanAskAgain();

        void ItShouldShowTheFailureRatherThanAskAgain() => result.Value.Single().State.ShouldBe(TopicState.CurationFailed);
    }

    [Test]
    public async Task TheAuthorEditsATopicWhileItIsCurated()
    {
        _state.Topics["projects/atlas"] = ResearchTopic.New("projects/atlas", Discovered) with { Queries = ["atlas"], Pinned = true };
        _state.FailedCurations["projects/atlas"] = new FailedCuration(new CurationRequest("projects/atlas", [], Discovered), "Pinned.", Discovered);

        Result<IReadOnlyList<TopicSummary>, Error> result = await _handler.Handle(new ListTopicsQuery(), CancellationToken.None);

        ItShouldLetTheEditSupersedeTheFailure();

        void ItShouldLetTheEditSupersedeTheFailure() => result.Value.Single().State.ShouldBe(TopicState.Idle);
    }

    [Test]
    public async Task ATopicsCurationFailed()
    {
        DateTimeOffset curated = Discovered.AddDays(1);
        _state.Topics["projects/atlas"] = ResearchTopic.New("projects/atlas", Discovered) with { LastCuratedAt = curated };
        _state.Topics["projects/beta"] = ResearchTopic.New("projects/beta", Discovered);
        CurationRequest failed = new("projects/atlas", [], Discovered);
        _state.FailedCurations["projects/atlas"] = new FailedCuration(failed, "No verdict.", Discovered);
        _state.FailedCurations["projects/beta"] = new FailedCuration(new CurationRequest("projects/beta", [], Discovered), "No verdict.", Discovered);
        _state.CurationRequests["projects/beta"] = new CurationRequest("projects/beta", ["projects/beta/index.md"], curated);

        Result<IReadOnlyList<TopicSummary>, Error> result = await _handler.Handle(new ListTopicsQuery(), CancellationToken.None);

        ItShouldShowTheFailure();
        ItShouldShowANewerRequestAsWaiting();
        ItShouldShowWhenEachWasLastCurated();

        void ItShouldShowTheFailure() => result.Value[0].State.ShouldBe(TopicState.CurationFailed);

        void ItShouldShowANewerRequestAsWaiting() => result.Value[1].State.ShouldBe(TopicState.AwaitingCuration);

        void ItShouldShowWhenEachWasLastCurated() => result.Value.Select(topic => topic.LastCuratedAt).ShouldBe([curated, null]);
    }

    [Test]
    public async Task APendingBatchIsMissingItsTopic()
    {
        _state.Topics["projects/atlas"] = ResearchTopic.New("projects/atlas", Discovered);
        _state.PendingBatches["edited-by-hand"] = new ResearchBatch(null!, Discovered, []);

        Result<IReadOnlyList<TopicSummary>, Error> result = await _handler.Handle(new ListTopicsQuery(), CancellationToken.None);

        ItShouldStillListTheTopics();

        void ItShouldStillListTheTopics() => result.Value.ShouldHaveSingleItem().State.ShouldBe(TopicState.Idle);
    }

    [Test]
    public async Task TopicsHaveBeenSearched()
    {
        DateTimeOffset searched = Discovered.AddDays(2);
        _state.Topics["projects/atlas"] = ResearchTopic.New("projects/atlas", Discovered) with { Queries = ["atlas"], LastSearchedAt = searched };
        _state.Topics["projects/beta"] = ResearchTopic.New("projects/beta", Discovered) with
        {
            Queries = ["beta"],
            LastSearchedAt = searched,
            Interval = TimeSpan.FromDays(4)
        };
        _state.Topics["projects/gamma"] = ResearchTopic.New("projects/gamma", Discovered);
        _state.CurationRequests["projects/beta"] = new CurationRequest("projects/beta", [], searched);
        _state.PendingBatches["projects/beta"] = new ResearchBatch(
            "projects/beta",
            searched,
            [
                new BatchResult("beta", "https://example.com/one", new SearchResult("https://example.com/one", "One", null, "SearXNG", null)),
                new BatchResult("beta", "https://example.com/two", new SearchResult("https://example.com/two", "Two", null, "SearXNG", null))
            ]);

        Result<IReadOnlyList<TopicSummary>, Error> result = await _handler.Handle(new ListTopicsQuery(), CancellationToken.None);

        ItShouldShowResultsWaitingForTriageFirst();
        ItShouldCountThePendingResults();
        ItShouldShowWhenEachWasSearchedAndIsNextDue();

        void ItShouldShowResultsWaitingForTriageFirst() =>
            result.Value.Select(topic => topic.State).ShouldBe([TopicState.Idle, TopicState.Triaging, TopicState.Idle]);

        void ItShouldCountThePendingResults() => result.Value.Select(topic => topic.PendingResults).ShouldBe([0, 2, 0]);

        // A topic with no queries has nothing to search, so it is never due.
        void ItShouldShowWhenEachWasSearchedAndIsNextDue() => result.Value.ShouldSatisfyAllConditions(
            topics => topics.Select(topic => topic.LastSearchedAt).ShouldBe([searched, searched, null]),
            topics => topics.Select(topic => topic.NextSearchAt).ShouldBe([searched.AddDays(1), searched.AddDays(4), null]));
    }
}
