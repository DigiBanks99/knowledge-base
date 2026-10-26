using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class DiscoverTopicsCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);

    private readonly ResearchTopics _state = new();
    private readonly DiscoverTopicsCommandHandler _handler;

    public DiscoverTopicsCommandHandlerTests()
    {
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        IWikiTopics wikiTopics = Substitute.For<IWikiTopics>();
        wikiTopics.All().Returns(["learning/rust.md", "plans/q4.md", "projects/atlas"]);
        _handler = new DiscoverTopicsCommandHandler(
            NullLogger<DiscoverTopicsCommandHandler>.Instance,
            FakeResearchStore.Holding(_state),
            wikiTopics,
            timeProvider);
    }

    [Test]
    public async Task TheAppStartsWithNoResearchState()
    {
        Result<IReadOnlyList<string>, Error> result = await _handler.Handle(new DiscoverTopicsCommand(), CancellationToken.None);

        ItShouldCreateEveryTopic();
        ItShouldRequestCurationForEveryTopic();
        ItShouldReportThem();

        void ItShouldCreateEveryTopic() =>
            _state.Topics.Keys.ShouldBe(["learning/rust.md", "plans/q4.md", "projects/atlas"], ignoreOrder: true);

        void ItShouldRequestCurationForEveryTopic() => _state.CurationRequests.Values.ShouldAllBe(request =>
            request.ChangedPaths.Count == 0 && request.RequestedAt == Now);

        void ItShouldReportThem() => result.Value.Count.ShouldBe(3);
    }

    [Test]
    public async Task TheAppStartsWithSomeTopicsKnown()
    {
        ResearchTopic atlas = ResearchTopic.New("projects/atlas", Now.AddDays(-1)) with { Muted = true };
        _state.Topics["projects/atlas"] = atlas;

        Result<IReadOnlyList<string>, Error> result = await _handler.Handle(new DiscoverTopicsCommand(), CancellationToken.None);

        ItShouldAdoptOnlyTheUnknownTopics();
        ItShouldLeaveTheKnownTopicAlone();

        void ItShouldAdoptOnlyTheUnknownTopics()
        {
            result.Value.ShouldBe(["learning/rust.md", "plans/q4.md"]);
            _state.CurationRequests.Keys.ShouldBe(["learning/rust.md", "plans/q4.md"], ignoreOrder: true);
        }

        void ItShouldLeaveTheKnownTopicAlone() => _state.Topics["projects/atlas"].ShouldBeSameAs(atlas);
    }
}
