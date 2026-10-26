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
}
