using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Queries;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Queries;

public sealed class ListJudgedResultsQueryHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstSearch = new(2026, 10, 20, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondSearch = new(2026, 10, 27, 7, 0, 0, TimeSpan.Zero);
    private const string Atlas = "projects/atlas";

    private readonly ResearchTopics _state = new();
    private readonly ListJudgedResultsQueryHandler _handler;

    public ListJudgedResultsQueryHandlerTests()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Discovered);
        _state.CompletedBatches.Add(new ResearchBatch(Atlas, FirstSearch,
        [
            Result("https://example.com/old", Verdict.Discard("Off topic.", FirstSearch) with { Useful = 0.2, Injection = 0.01 }) with { Mark = ResultMark.Noise }
        ]));
        _state.PendingBatches[Atlas] = new ResearchBatch(Atlas, SecondSearch,
        [
            Result("https://example.com/new", Verdict.Keep(null, ["A quote."], SecondSearch) with { Useful = 0.8, Injection = 0.02, Kind = "release" }),
            Result("https://example.com/waiting", null),
            Result("https://example.com/unfetched", Verdict.Discard("The page could not be fetched: 404.", SecondSearch))
        ]);
        _state.CompletedBatches.Add(new ResearchBatch("projects/other", SecondSearch,
        [
            Result("https://example.com/other", Verdict.Discard("Off topic.", SecondSearch))
        ]));
        WikiPaths wikiPaths = new(Options.Create(new WikiConfigurationSettings { RootDirectory = "/data/kb" }));
        _handler = new ListJudgedResultsQueryHandler(
            FakeResearchStore.Holding(_state),
            wikiPaths,
            Options.Create(new ResearchSettings { KeepThreshold = 0.6, InjectionThreshold = 0.9 }));
    }

    [Test]
    public async Task TheAuthorOpensATopicsJudgedResults()
    {
        Result<JudgedResults, Error> judged = await _handler.Handle(new ListJudgedResultsQuery("projects--atlas"), CancellationToken.None);

        ItShouldListOnlyTheTopicsJudgedResultsNewestFirst();
        ItShouldOfferToMarkOnlyTheKeepersVerdicts();
        ItShouldShowTheKeepersProbabilitiesBesideTheMark();
        ItShouldShowTheThresholdsTheyAreHeldTo();

        void ItShouldListOnlyTheTopicsJudgedResultsNewestFirst() => judged.Value.Results.Select(result => result.Url)
            .ShouldBe(["https://example.com/new", "https://example.com/unfetched", "https://example.com/old"]);

        void ItShouldOfferToMarkOnlyTheKeepersVerdicts() =>
            judged.Value.Results.Select(result => result.Markable).ShouldBe([true, false, true]);

        void ItShouldShowTheKeepersProbabilitiesBesideTheMark()
        {
            judged.Value.Results[0].ShouldSatisfyAllConditions(
                result => result.Kept.ShouldBeTrue(),
                result => result.Useful.ShouldBe(0.8),
                result => result.Injection.ShouldBe(0.02),
                result => result.Kind.ShouldBe("release"),
                result => result.Mark.ShouldBeNull(),
                result => result.SearchedAt.ShouldBe(SecondSearch));
            judged.Value.Results[2].ShouldSatisfyAllConditions(
                result => result.Kept.ShouldBeFalse(),
                result => result.Reason.ShouldBe("Off topic."),
                result => result.Useful.ShouldBe(0.2),
                result => result.Mark.ShouldBe(ResultMark.Noise));
        }

        void ItShouldShowTheThresholdsTheyAreHeldTo() => judged.Value.ShouldSatisfyAllConditions(
            results => results.KeepThreshold.ShouldBe(0.6),
            results => results.InjectionThreshold.ShouldBe(0.9),
            results => results.RetentionDays.ShouldBe(30),
            results => results.AgreementWindowDays.ShouldBe(7));
    }

    private static BatchResult Result(string url, Verdict? verdict) =>
        new("atlas release", url, new SearchResult(url, "A page", null, "SearXNG", null), Verdict: verdict);
}
