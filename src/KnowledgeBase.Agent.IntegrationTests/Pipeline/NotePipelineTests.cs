using KnowledgeBase.Agent.Facts.Tools;
using KnowledgeBase.Agent.Notes.Commands;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Pipeline;

/// <summary>
/// A note's journey from capture to the wiki, with the agents scripted: the folder a note or fact file is in is its
/// state, so these assert on where the files end up.
/// </summary>
public sealed class NotePipelineTests : IAsyncDisposable
{
    private const string NoteName = "note-2026-10-24-101500.md";
    private const string NoteContent = "Susan gave feedback on the design review: be clearer about trade-offs.";
    private const string Collection = "feedback-from-susan";

    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task ANoteIsCaptured()
    {
        _wiki.FactFinderWritesFacts(Collection);
        await StartAsync();

        await CaptureAsync(NoteName, NoteContent);
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).Count == 1 && await _wiki.RunsFinishedAsync(2));
        IReadOnlyList<AgentRun> runs = await _wiki.RunsAsync();

        ItShouldFinish();
        ItShouldFileTheNoteAsProcessed();
        ItShouldFileTheFactsAsProcessed();
        ItShouldGiveTheNoteToTheFactFinder();
        ItShouldGiveTheFactsAndTheirNoteToTheThreadWeaver();
        ItShouldRecordBothRuns();

        void ItShouldFinish() => settled.ShouldBeTrue();

        void ItShouldFileTheNoteAsProcessed()
        {
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).ShouldBe([NoteName]);
            TestWiki.FileNames(_wiki.Settings.NewNotesDirectory).ShouldBeEmpty();
            TestWiki.FileNames(_wiki.Settings.ProcessingNotesDirectory).ShouldBeEmpty();
        }

        void ItShouldFileTheFactsAsProcessed()
        {
            TestWiki.FileNames(_wiki.Settings.ProcessedFactsDirectory).ShouldBe([$"{Collection}.json"]);
            TestWiki.FileNames(_wiki.Settings.FactsDirectory).ShouldBeEmpty();
        }

        void ItShouldGiveTheNoteToTheFactFinder() => _wiki.FactFinder.Prompts.ShouldHaveSingleItem().ShouldContain(NoteContent);

        void ItShouldGiveTheFactsAndTheirNoteToTheThreadWeaver()
        {
            string prompt = _wiki.ThreadWeaver.Prompts.ShouldHaveSingleItem();
            prompt.ShouldContain("Susan");
            prompt.ShouldContain($"\"sourceNote\":\"{NoteName}\"");
        }

        void ItShouldRecordBothRuns()
        {
            AgentRun factFinder = runs.Single(run => run.Agent == KnownAgents.FactFinder);
            factFinder.Status.ShouldBe(AgentRunStatus.Succeeded);
            factFinder.Trigger.ShouldBe(new AgentRunTrigger(AgentRunTriggerKind.Note, NoteName));
            factFinder.ToolCalls.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                call => call.Tool.ShouldBe(nameof(FactTools.FactWriterTool)),
                call => call.Target.ShouldBe(Collection),
                call => call.Succeeded.ShouldBeTrue());

            AgentRun threadWeaver = runs.Single(run => run.Agent == KnownAgents.ThreadWeaver);
            threadWeaver.Status.ShouldBe(AgentRunStatus.Succeeded);
            threadWeaver.Trigger.ShouldBe(new AgentRunTrigger(AgentRunTriggerKind.Facts, $"{Collection}.json"));
        }
    }

    [Test]
    public async Task TheFactFinderFindsNoFacts()
    {
        await StartAsync();

        await CaptureAsync(NoteName, NoteContent);
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.FailedNotesDirectory).Count == 1 && await _wiki.RunsFinishedAsync(1));

        ItShouldFinish();
        ItShouldParkTheNoteAsFailed();
        ItShouldNotWeaveAnything();

        void ItShouldFinish() => settled.ShouldBeTrue();

        void ItShouldParkTheNoteAsFailed()
        {
            TestWiki.FileNames(_wiki.Settings.FailedNotesDirectory).ShouldBe([NoteName]);
            TestWiki.FileNames(_wiki.Settings.ProcessingNotesDirectory).ShouldBeEmpty();
        }

        void ItShouldNotWeaveAnything()
        {
            TestWiki.FileNames(_wiki.Settings.FactsDirectory).ShouldBeEmpty();
            _wiki.ThreadWeaver.Prompts.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task TheFactFinderFails()
    {
        _wiki.FactFinder.Fails("The model is unavailable.");
        await StartAsync();

        await CaptureAsync(NoteName, NoteContent);
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.FailedNotesDirectory).Count == 1 && await _wiki.RunsFinishedAsync(1));
        IReadOnlyList<AgentRun> runs = await _wiki.RunsAsync();

        ItShouldFinish();
        ItShouldParkTheNoteAsFailed();
        ItShouldRecordWhyTheRunFailed();

        void ItShouldFinish() => settled.ShouldBeTrue();

        void ItShouldParkTheNoteAsFailed() =>
            TestWiki.FileNames(_wiki.Settings.FailedNotesDirectory).ShouldBe([NoteName]);

        void ItShouldRecordWhyTheRunFailed() => runs.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            run => run.Status.ShouldBe(AgentRunStatus.Failed),
            run => run.Error.ShouldBe("The model is unavailable."),
            run => run.CompletedAt.ShouldNotBeNull());
    }

    [Test]
    public async Task TheThreadWeaverFails()
    {
        _wiki.FactFinderWritesFacts(Collection);
        _wiki.ThreadWeaver.Fails("The model is unavailable.");
        await StartAsync();

        await CaptureAsync(NoteName, NoteContent);
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.FailedNotesDirectory).Count == 1 && await _wiki.RunsFinishedAsync(2));
        IReadOnlyList<AgentRun> runs = await _wiki.RunsAsync();

        ItShouldFinish();
        ItShouldParkTheFactsAsFailed();
        ItShouldParkTheNoteAsFailed();
        ItShouldRecordTheFailedRun();

        void ItShouldFinish() => settled.ShouldBeTrue();

        void ItShouldParkTheFactsAsFailed()
        {
            TestWiki.FileNames(_wiki.Settings.FailedFactsDirectory).ShouldBe([$"{Collection}.json"]);
            TestWiki.FileNames(_wiki.Settings.FactsDirectory).ShouldBeEmpty();
        }

        void ItShouldParkTheNoteAsFailed()
        {
            TestWiki.FileNames(_wiki.Settings.FailedNotesDirectory).ShouldBe([NoteName]);
            TestWiki.FileNames(_wiki.Settings.ProcessingNotesDirectory).ShouldBeEmpty();
        }

        void ItShouldRecordTheFailedRun() =>
            runs.Single(run => run.Agent == KnownAgents.ThreadWeaver).Status.ShouldBe(AgentRunStatus.Failed);
    }

    [Test]
    public async Task TwoNotesAreCapturedTogether()
    {
        const string secondNote = "note-2026-10-24-101600.md";
        TaskCompletionSource firstNoteMayFinish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int factFinderRuns = 0;
        _wiki.FactFinder.Behaviour = async (_, cancellationToken) =>
        {
            int run = Interlocked.Increment(ref factFinderRuns);
            if (run == 1)
            {
                await firstNoteMayFinish.Task.WaitAsync(cancellationToken);
            }

            string collection = $"facts-{run}";
            return await _wiki.CallToolAsync(
                nameof(FactTools.FactWriterTool),
                collection,
                () => _wiki.Get<FactTools>().FactWriterTool(AgentScripts.SampleFacts, collection, cancellationToken));
        };
        await StartAsync();

        await CaptureAsync(NoteName, NoteContent);
        await CaptureAsync(secondNote, "A second thought.");
        bool firstNoteStarted = await Eventually.Until(() => _wiki.FactFinder.Prompts.Count == 1);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        int notesReadWhileTheFirstWasProcessing = _wiki.FactFinder.Prompts.Count;
        firstNoteMayFinish.SetResult();
        bool settled = await Eventually.Until(
            () => TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).Count == 2,
            TimeSpan.FromSeconds(30));

        ItShouldStartWithOneNote();
        ItShouldProcessOneNoteAtATime();
        ItShouldProcessBothNotes();

        void ItShouldStartWithOneNote() => firstNoteStarted.ShouldBeTrue();

        void ItShouldProcessOneNoteAtATime() => notesReadWhileTheFirstWasProcessing.ShouldBe(1);

        void ItShouldProcessBothNotes()
        {
            settled.ShouldBeTrue();
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).ShouldBe([NoteName, secondNote]);
        }
    }

    private async Task StartAsync()
    {
        await _wiki.InitialiseAsync();
        _wiki.Start();
    }

    private async Task CaptureAsync(string noteName, string content)
    {
        Result<string, Error> captured = await _wiki.Get<CaptureWikiNoteCommandHandler>()
            .Handle(new CaptureWikiNoteCommand(Path.GetFileNameWithoutExtension(noteName), content), CancellationToken.None);
        captured.IsSuccess.ShouldBeTrue();
    }
}