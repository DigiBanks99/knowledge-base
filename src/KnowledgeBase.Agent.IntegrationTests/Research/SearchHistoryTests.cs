using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Queries;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Research;

/// <summary>A topic's searches in its run history, next to the agent runs, and what became of their results.</summary>
public sealed class SearchHistoryTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 1, 7, 0, 0, TimeSpan.Zero);
    private const string Topic = "projects/garden";
    private const string Slug = "projects--garden";
    private const string Beds = "https://example.com/beds";
    private const string Spam = "https://example.com/spam";

    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task ATopicIsSearchedAndItsResultsTriaged()
    {
        await _wiki.InitialiseAsync();
        IResearchStore store = _wiki.Get<IResearchStore>();
        await store.UpdateTopicAsync(
            Topic,
            _ => Maybe.From(ResearchTopic.New(Topic, Discovered) with { Brief = "News about raised-bed gardening.", Queries = ["raised beds"] }),
            CancellationToken.None);
        _wiki.Search.Results =
        [
            new SearchResult(Beds, "Raised beds", null, FakeSearchSource.SourceName, null),
            new SearchResult(Spam, "Ten tricks", null, FakeSearchSource.SourceName, null)
        ];
        _wiki.Keeper.Screen = page => new Screening(page.Url == Beds ? 0.8 : 0.2, 0.1);

        await _wiki.Get<SearchTopicsCommandHandler>().Handle(new SearchTopicsCommand(), CancellationToken.None);
        TopicRunsPage afterSearch = await HistoryAsync();
        // Fetching would reach the web, so the pages are laid in the batch as the fetch step leaves them.
        await store.UpdatePendingBatchAsync(
            Topic,
            batch => batch with { Results = [.. batch.Results.Select(result => result with { Page = FetchedPage.Content(Discovered, $"{result.Result.Title}: a page long enough to be a paragraph.", false) })] },
            CancellationToken.None);
        await _wiki.Get<TriageResultsCommandHandler>().Handle(new TriageResultsCommand(), CancellationToken.None);
        TopicRunsPage afterTriage = await HistoryAsync();

        ItShouldShowTheSearchInTheTopicsHistory();
        ItShouldShowTheSearchNextToTheKeeperCallsAndTheClerkRun();
        ItShouldAddWhatBecameOfTheResultsToTheSearch();

        void ItShouldShowTheSearchInTheTopicsHistory() => afterSearch.Runs.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            run => run.Agent.ShouldBe(SearchRuns.Agent),
            run => run.Trigger.ShouldBe(new AgentRunTrigger(AgentRunTriggerKind.Search, "Scheduled")),
            run => run.Status.ShouldBe(AgentRunStatus.Succeeded),
            run => run.ToolCalls.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                call => call.Target.ShouldBe("raised beds"),
                call => call.Detail.ShouldBe($"{FakeSearchSource.SourceName}: 2 results")));

        void ItShouldShowTheSearchNextToTheKeeperCallsAndTheClerkRun() => afterTriage.Runs.Select(run => run.Agent).ShouldBe(
            ["Keeper", "Keeper", "Keeper", KnownAgents.Clerk, SearchRuns.Agent],
            ignoreOrder: true);

        void ItShouldAddWhatBecameOfTheResultsToTheSearch() => afterTriage.Runs
            .Single(run => run.Agent == SearchRuns.Agent)
            .ToolCalls.Select(call => (call.Tool, call.Target))
            .ShouldBe(
            [
                (SearchRuns.SearchStep, "raised beds"),
                (SearchRuns.FetchStep, Beds),
                (SearchRuns.FetchStep, Spam),
                (SearchRuns.KeepStep, Beds),
                (SearchRuns.DiscardStep, Spam),
                (SearchRuns.NoteStep, TestWiki.FileNames(_wiki.Settings.NewNotesDirectory).Single())
            ]);
    }

    private async Task<TopicRunsPage> HistoryAsync()
    {
        Result<TopicRunsPage, Error> page = await _wiki.Get<ListTopicRunsQueryHandler>()
            .Handle(new ListTopicRunsQuery(Slug, 1), CancellationToken.None);
        return page.Value;
    }
}
