using KnowledgeBase.Agent.Notes;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Research;

/// <summary>The Keeper judging a fetched batch, the Clerk summarising what it kept, and the note that lands in the notes inbox.</summary>
public sealed class TriageTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Searched = new(2026, 10, 27, 7, 0, 0, TimeSpan.Zero);
    private const string Topic = "projects/garden";
    private const string Beds = "https://example.com/beds";
    private const string Spam = "https://example.com/spam";
    private const string Drainage = "Raised beds drain better than ground beds after heavy autumn rain.";
    private const string Summary = "Raised beds drain better than ground beds in a wet autumn.";

    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task TheKeeperKeepsOneFetchedResult()
    {
        // Not started, so the note watcher leaves the note in the inbox for the test to read.
        await _wiki.InitialiseAsync();
        IResearchStore store = _wiki.Get<IResearchStore>();
        await store.UpdateTopicAsync(
            Topic,
            _ => Maybe.From(ResearchTopic.New(Topic, Searched) with { Brief = "News about raised-bed gardening.", Queries = ["raised beds"] }),
            CancellationToken.None);
        await store.WritePendingBatchAsync(
            new ResearchBatch(Topic, Searched, [Result(Beds, "Raised beds"), Result(Spam, "Ten tricks")]),
            CancellationToken.None);
        _wiki.Keeper.Screen = page => new Screening(page.Url == Beds ? 0.85 : 0.2, 0.1);
        _wiki.Clerk.Behaviour = async (_, cancellationToken) =>
        {
            await _wiki.CallToolAsync(
                nameof(ClerkTools.RecordSummary),
                null,
                () => _wiki.Get<ClerkTools>().RecordSummary(Summary, cancellationToken));
            return "Recorded the summary.";
        };

        Result<TriageCycle, Error> cycle = await _wiki.Get<TriageResultsCommandHandler>()
            .Handle(new TriageResultsCommand(), CancellationToken.None);
        IReadOnlyList<AgentRun> runs = await _wiki.RunsAsync();
        string[] notes = TestWiki.FileNames(_wiki.Settings.NewNotesDirectory).ToArray();

        ItShouldMoveTheBatchToAwaiting();
        ItShouldDropOneResearchNoteInTheInbox();
        ItShouldRecordEachKeeperCallAsARun();
        ItShouldRunTheClerkForTheKeptResultOnly();

        void ItShouldMoveTheBatchToAwaiting()
        {
            cycle.Value.Awaiting.ShouldBe([Topic]);
            TestWiki.FileNames(_wiki.Settings.PendingBatchesDirectory).ShouldBeEmpty();
            TestWiki.FileNames(_wiki.Settings.AwaitingBatchesDirectory).ShouldBe(["projects--garden.json"]);
        }

        void ItShouldDropOneResearchNoteInTheInbox()
        {
            notes.ShouldBe(["research-projects--garden-20261027-070000.md"]);
            string content = File.ReadAllText(Path.Combine(_wiki.Settings.NewNotesDirectory, notes[0]));
            Result<NoteProvenance, Error> provenance = ResearchNoteHeader.Parse(content);
            provenance.Value.Topic.ShouldBe(Topic);
            provenance.Value.Sources.ShouldBe([Beds]);
            content.ShouldContain("## Raised beds");
            content.ShouldContain($"Source: {Beds}");
            content.ShouldContain("Kind: release");
            content.ShouldContain(Summary);
            content.ShouldContain($"> {Drainage}");
        }

        void ItShouldRecordEachKeeperCallAsARun() => runs.Where(run => run.Agent == KeeperPolicy.Agent).ShouldSatisfyAllConditions(
            keeper => keeper.Count().ShouldBe(3),
            keeper => keeper.ShouldAllBe(run => run.Trigger.Kind == AgentRunTriggerKind.Triage
                && run.Status == AgentRunStatus.Succeeded
                && run.Topic == Topic
                && run.ToolCalls.Count == 0));

        void ItShouldRunTheClerkForTheKeptResultOnly()
        {
            _wiki.Clerk.Prompts.ShouldHaveSingleItem().ShouldContain($"URL: {Beds}");
            AgentRun clerk = runs.Single(run => run.Agent == KnownAgents.Clerk);
            clerk.Status.ShouldBe(AgentRunStatus.Succeeded);
            clerk.Trigger.Reference.ShouldBe(Beds);
            clerk.ToolCalls.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                call => call.Tool.ShouldBe(nameof(ClerkTools.RecordSummary)),
                call => call.Succeeded.ShouldBeTrue());
        }
    }

    private static BatchResult Result(string url, string title) =>
        new("raised beds", url, new SearchResult(url, title, null, "SearXNG", null), FetchedPage.Content(Searched, $"# {title}\n\n{Drainage}", false));
}
