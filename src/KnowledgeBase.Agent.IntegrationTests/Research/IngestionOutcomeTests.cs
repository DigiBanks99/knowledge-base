using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Facts.Tools;
using KnowledgeBase.Agent.Files.Tools;
using KnowledgeBase.Agent.Notes.Commands;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Research;

/// <summary>
/// A research note's journey through the real pipeline with scripted agents, and the topic's interval following what
/// became of it. Resolution reads only the notes folders and the run store, so each test also stands for a restart.
/// </summary>
public sealed class IngestionOutcomeTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Searched = new(2026, 10, 27, 7, 0, 0, TimeSpan.Zero);
    private const string Topic = "projects/knowledge-base";
    private const string Source = "https://example.com/agent-framework-2";
    private const string Collection = "agent-framework";
    private const string NoteName = "research-projects--knowledge-base-20261027-070000.md";
    private const string ResearchPage = "projects/knowledge-base/agent-framework.md";

    private static readonly string Note =
        $"---\norigin: research\ntopic: {Topic}\nsources:\n  - {Source}\n---\n\nVersion 2 of the agent framework adds workflows.";

    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task TheThreadWeaverFilesTheResearchNote()
    {
        _wiki.ThreadWeaver.Behaviour = async (_, cancellationToken) =>
        {
            await _wiki.CallToolAsync(
                nameof(FileTools.CreateFile),
                ResearchPage,
                () => _wiki.Get<FileTools>().CreateFile(ResearchPage, $"# Agent Framework\n\nVersion 2 adds workflows ({Source}).\n", cancellationToken: cancellationToken));
            return "Filed the research.";
        };
        await AwaitIngestionAsync(NoteName, _wiki.Settings.ProcessedNotesDirectory);

        Result<ResolvedBatches, Error> resolved = await _wiki.Get<ResolveAwaitingBatchesCommandHandler>()
            .Handle(new ResolveAwaitingBatchesCommand(), CancellationToken.None);
        ResearchTopic topic = (await _wiki.Get<IResearchStore>().ReadTopicAsync(Topic, CancellationToken.None)).Value.Value;
        AgentRun threadWeaver = (await _wiki.RunsAsync()).Single(run => run.Agent == KnownAgents.ThreadWeaver);

        ItShouldRecordTheNoteOnTheThreadWeaversRun();
        ItShouldCheckTheTopicDailyAgain();
        ItShouldMoveTheBatchToCompleted();

        void ItShouldRecordTheNoteOnTheThreadWeaversRun() => threadWeaver.SourceNote.ShouldBe(NoteName);

        void ItShouldCheckTheTopicDailyAgain()
        {
            resolved.Value.Positive.ShouldBe([Topic]);
            topic.Interval.ShouldBe(TimeSpan.FromHours(24));
            topic.LastPositiveAt.ShouldNotBeNull();
        }

        void ItShouldMoveTheBatchToCompleted()
        {
            TestWiki.FileNames(_wiki.Settings.AwaitingBatchesDirectory).ShouldBeEmpty();
            TestWiki.FileNames(_wiki.Settings.CompletedBatchesDirectory).ShouldBe(["projects--knowledge-base-20261027-070000.json"]);
        }
    }

    [Test]
    public async Task TheThreadWeaverFilesNothingFromTheResearchNote()
    {
        _wiki.ThreadWeaver.Behaviour = (_, _) => Task.FromResult("Nothing worth filing.");
        await AwaitIngestionAsync(NoteName, _wiki.Settings.ProcessedNotesDirectory);

        Result<ResolvedBatches, Error> resolved = await _wiki.Get<ResolveAwaitingBatchesCommandHandler>()
            .Handle(new ResolveAwaitingBatchesCommand(), CancellationToken.None);
        ResearchTopic topic = (await _wiki.Get<IResearchStore>().ReadTopicAsync(Topic, CancellationToken.None)).Value.Value;

        ItShouldBackTheTopicOff();

        void ItShouldBackTheTopicOff()
        {
            resolved.Value.Empty.ShouldBe([Topic]);
            topic.Interval.ShouldBe(TimeSpan.FromHours(48));
            topic.LastPositiveAt.ShouldBeNull();
        }
    }

    [Test]
    public async Task TheThreadWeaverFailsOnTheResearchNote()
    {
        _wiki.ThreadWeaver.Fails("The model is unavailable.");
        await AwaitIngestionAsync(NoteName, _wiki.Settings.FailedNotesDirectory);

        Result<ResolvedBatches, Error> resolved = await _wiki.Get<ResolveAwaitingBatchesCommandHandler>()
            .Handle(new ResolveAwaitingBatchesCommand(), CancellationToken.None);
        ResearchTopic topic = (await _wiki.Get<IResearchStore>().ReadTopicAsync(Topic, CancellationToken.None)).Value.Value;

        ItShouldCountTheFailureWithoutMovingTheInterval();
        ItShouldFreeTheTopic();

        void ItShouldCountTheFailureWithoutMovingTheInterval()
        {
            resolved.Value.Failed.ShouldBe([Topic]);
            topic.ConsecutiveFailures.ShouldBe(1);
            topic.Interval.ShouldBeNull();
        }

        void ItShouldFreeTheTopic() => TestWiki.FileNames(_wiki.Settings.AwaitingBatchesDirectory).ShouldBeEmpty();
    }

    [Test]
    public async Task TheResearchNoteIsStillBeingProcessed()
    {
        // Started with the topic and its awaiting batch laid out but no note, as after a restart before the note arrived.
        await PrepareAwaitingBatchAsync(NoteName);
        _wiki.Write(_wiki.Settings.ProcessingNotesDirectory, NoteName, Note);

        Result<ResolvedBatches, Error> resolved = await _wiki.Get<ResolveAwaitingBatchesCommandHandler>()
            .Handle(new ResolveAwaitingBatchesCommand(), CancellationToken.None);

        ItShouldKeepWaiting();

        void ItShouldKeepWaiting()
        {
            resolved.Value.Positive.ShouldBeEmpty();
            resolved.Value.Empty.ShouldBeEmpty();
            resolved.Value.Failed.ShouldBeEmpty();
            TestWiki.FileNames(_wiki.Settings.AwaitingBatchesDirectory).ShouldBe(["projects--knowledge-base.json"]);
        }
    }

    /// <summary>Lays out the topic and its awaiting batch, then lets the note run through the started pipeline.</summary>
    private async Task AwaitIngestionAsync(string noteName, string finalDirectory)
    {
        _wiki.FactFinder.Behaviour = async (_, cancellationToken) => await _wiki.CallToolAsync(
            nameof(FactTools.FactWriterTool),
            Collection,
            () => _wiki.Get<FactTools>().FactWriterTool(
                [new Fact(new FactSubject("Agent Framework", "A library for building agents.", SubjectKind.Other), [new Observation("Version 2 was released", "The release adds workflows.", 2, [], [Source])])],
                Collection,
                cancellationToken));
        await PrepareAwaitingBatchAsync(noteName);
        // The topic's page exists before the pipeline runs, or the run's end would prune the topic as gone from the wiki.
        _wiki.Write(Path.Combine(_wiki.Settings.WikiDirectory, "projects", "knowledge-base"), "index.md", "# Knowledge base\n");
        _wiki.Start();

        Result<string, Error> captured = await _wiki.Get<CaptureWikiNoteCommandHandler>()
            .Handle(new CaptureWikiNoteCommand(Path.GetFileNameWithoutExtension(noteName), Note), CancellationToken.None);
        captured.IsSuccess.ShouldBeTrue();
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(finalDirectory).Contains(noteName) && await _wiki.RunsFinishedAsync(2));
        settled.ShouldBeTrue(settled ? string.Empty : await _wiki.DescribeAsync());
    }

    private async Task PrepareAwaitingBatchAsync(string noteName)
    {
        await _wiki.InitialiseAsync();
        IResearchStore store = _wiki.Get<IResearchStore>();
        await store.UpdateTopicAsync(
            Topic,
            _ => Maybe.From(ResearchTopic.New(Topic, Searched) with { Brief = "A personal wiki.", Queries = ["agent framework"], LastSearchedAt = Searched }),
            CancellationToken.None);
        await store.WritePendingBatchAsync(new ResearchBatch(Topic, Searched, []), CancellationToken.None);
        await store.AwaitBatchAsync(Topic, noteName, Searched, CancellationToken.None);
    }
}
