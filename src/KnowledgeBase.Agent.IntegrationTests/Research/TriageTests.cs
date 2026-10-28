using KnowledgeBase.Agent.Notes;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Research;

/// <summary>The Scout judging a fetched batch with its real tools, and the note that lands in the notes inbox.</summary>
public sealed class TriageTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Searched = new(2026, 10, 27, 7, 0, 0, TimeSpan.Zero);
    private const string Topic = "projects/garden";
    private const string Beds = "https://example.com/beds";
    private const string Spam = "https://example.com/spam";

    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task TheScoutKeepsOneFetchedResult()
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
        _wiki.Scout.Behaviour = async (prompt, cancellationToken) =>
        {
            await (prompt.Contains(Beds)
                ? _wiki.CallToolAsync(nameof(ScoutTools.KeepResult), null, () => _wiki.Get<ScoutTools>().KeepResult("Raised beds drain better.", ["Raised beds drain better."], cancellationToken))
                : _wiki.CallToolAsync(nameof(ScoutTools.DiscardResult), null, () => _wiki.Get<ScoutTools>().DiscardResult("SEO spam.", cancellationToken)));
            return "Judged.";
        };

        Result<TriageCycle, Error> cycle = await _wiki.Get<TriageResultsCommandHandler>()
            .Handle(new TriageResultsCommand(), CancellationToken.None);
        IReadOnlyList<AgentRun> runs = await _wiki.RunsAsync();
        string[] notes = TestWiki.FileNames(_wiki.Settings.NewNotesDirectory).ToArray();

        ItShouldMoveTheBatchToAwaiting();
        ItShouldDropOneResearchNoteInTheInbox();
        ItShouldRecordOneRunPerResult();

        void ItShouldMoveTheBatchToAwaiting()
        {
            cycle.Value.Awaiting.ShouldBe([Topic]);
            TestWiki.FileNames(_wiki.Settings.PendingBatchesDirectory).ShouldBeEmpty();
            TestWiki.FileNames(_wiki.Settings.AwaitingBatchesDirectory).ShouldBe(["projects--garden.json"]);
        }

        void ItShouldDropOneResearchNoteInTheInbox()
        {
            notes.ShouldBe(["research-projects--garden-20261027-070000.md"]);
            Result<NoteProvenance, Error> provenance = ResearchNoteHeader.Parse(File.ReadAllText(Path.Combine(_wiki.Settings.NewNotesDirectory, notes[0])));
            provenance.Value.Topic.ShouldBe(Topic);
            provenance.Value.Sources.ShouldBe([Beds]);
        }

        void ItShouldRecordOneRunPerResult() => runs.ShouldSatisfyAllConditions(
            all => all.Count.ShouldBe(2),
            all => all.ShouldAllBe(run => run.Agent == KnownAgents.Scout && run.Trigger.Kind == AgentRunTriggerKind.Triage));
    }

    private static BatchResult Result(string url, string title) =>
        new("raised beds", url, new SearchResult(url, title, null, "SearXNG", null), FetchedPage.Content(Searched, $"Page of {title}.", false));
}
