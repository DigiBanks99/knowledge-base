using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Queries;
using KnowledgeBase.Agent.Research.Triage;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Queries;

public sealed class GetResearchQualityQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private const string Atlas = "projects/atlas";

    private readonly ResearchTopics _state = new();
    private readonly GetResearchQualityQueryHandler _handler;

    public GetResearchQualityQueryHandlerTests()
    {
        _state.CompletedBatches.Add(new ResearchBatch(Atlas, Now.AddDays(-2), [Result(Now.AddDays(-2), ResultMark.Useful)])
        {
            Outcome = IngestionOutcome.Positive,
            ResolvedAt = Now.AddDays(-1)
        });
        _state.CompletedBatches.Add(new ResearchBatch(Atlas, Now.AddDays(-20), [Result(Now.AddDays(-20), ResultMark.Noise)])
        {
            Outcome = IngestionOutcome.Empty,
            ResolvedAt = Now.AddDays(-19)
        });
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _handler = new GetResearchQualityQueryHandler(
            FakeResearchStore.Holding(_state),
            Options.Create(new ResearchSettings { KeeperModel = "laya-q8" }),
            timeProvider);
    }

    [Test]
    public async Task TheAuthorOpensTheStatusPage()
    {
        Result<ResearchQuality, Error> quality = await _handler.Handle(new GetResearchQualityQuery(), CancellationToken.None);

        ItShouldLabelTheFiguresWithTheConfiguredKeeperModel();
        ItShouldCountOnlyTheLastSevenDays();

        void ItShouldLabelTheFiguresWithTheConfiguredKeeperModel() => quality.Value.KeeperModel.ShouldBe("laya-q8");

        void ItShouldCountOnlyTheLastSevenDays() => quality.Value.ShouldSatisfyAllConditions(
            figures => figures.Marked.ShouldBe(1),
            figures => figures.Agreed.ShouldBe(1),
            figures => figures.NotesFiled.ShouldBe(1),
            figures => figures.NotesEmpty.ShouldBe(0));
    }

    private static BatchResult Result(DateTimeOffset judgedAt, ResultMark mark) =>
        new("atlas release", "https://example.com/page", new SearchResult("https://example.com/page", "A page", null, "SearXNG", null))
        {
            Verdict = Verdict.Keep("A summary.", [], judgedAt) with { Useful = 0.8 },
            Mark = mark
        };
}
