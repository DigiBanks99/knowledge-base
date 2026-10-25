using System.Text.Json;

using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Facts.Tools;
using KnowledgeBase.Agent.Files.Tools;
using KnowledgeBase.Agent.Notes.Commands;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Models;
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
    private const string ResearchTopic = "projects/knowledge-base";
    private const string ResearchSource = "https://example.com/agent-framework-2";
    private const string ProjectPage = "projects/knowledge-base/index.md";
    private const string ProjectPageContent = "# Knowledge base\n\nThe author's personal wiki.\n";
    private const string MovedPage = "projects/knowledge-base/overview.md";
    private const string ResearchPage = "projects/knowledge-base/agent-framework.md";
    private const string PersonalPage = "people/agent-framework/index.md";
    private const string ResearchNote =
        $"---\norigin: research\ntopic: {ResearchTopic}\nsources:\n  - {ResearchSource}\n---\n\nVersion 2 of the agent framework adds workflows.";

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
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        IReadOnlyList<AgentRun> runs = await _wiki.RunsAsync();

        ItShouldFinish();
        ItShouldFileTheNoteAsProcessed();
        ItShouldFileTheFactsAsProcessed();
        ItShouldGiveTheNoteToTheFactFinder();
        ItShouldGiveTheFactsAndTheirNoteToTheThreadWeaver();
        ItShouldRecordBothRuns();

        void ItShouldFinish() => settled.ShouldBeTrue(pipeline);

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
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();

        ItShouldFinish();
        ItShouldParkTheNoteAsFailed();
        ItShouldNotWeaveAnything();

        void ItShouldFinish() => settled.ShouldBeTrue(pipeline);

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
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        IReadOnlyList<AgentRun> runs = await _wiki.RunsAsync();

        ItShouldFinish();
        ItShouldParkTheNoteAsFailed();
        ItShouldRecordWhyTheRunFailed();

        void ItShouldFinish() => settled.ShouldBeTrue(pipeline);

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
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        IReadOnlyList<AgentRun> runs = await _wiki.RunsAsync();

        ItShouldFinish();
        ItShouldParkTheFactsAsFailed();
        ItShouldParkTheNoteAsFailed();
        ItShouldRecordTheFailedRun();

        void ItShouldFinish() => settled.ShouldBeTrue(pipeline);

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
    public async Task AResearchNoteIsCaptured()
    {
        _wiki.FactFinder.Behaviour = async (_, cancellationToken) => await _wiki.CallToolAsync(
            nameof(FactTools.FactWriterTool),
            Collection,
            () => _wiki.Get<FactTools>().FactWriterTool(AgentFrameworkFacts(ResearchSource, SubjectKind.Organisation), Collection, cancellationToken));
        await StartAsync();

        await CaptureAsync(NoteName, ResearchNote);
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).Count == 1 && await _wiki.RunsFinishedAsync(2));
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        FactDocument? document = settled ? await ReadProcessedFactsAsync() : null;
        DenyList denyList = await ReadDenyListAsync();

        ItShouldFinish();
        ItShouldTellTheFactFinderItIsResearch();
        ItShouldRecordTheFactsAsResearchOnTheTopic();
        ItShouldKeepTheSourcesOfEachObservation();
        ItShouldNotDenyTheOrganisationsItNames();

        void ItShouldFinish() => settled.ShouldBeTrue(pipeline);

        void ItShouldTellTheFactFinderItIsResearch() => _wiki.FactFinder.Prompts.ShouldHaveSingleItem()
            .ShouldContain($"research note about the wiki topic `{ResearchTopic}`", Case.Sensitive);

        void ItShouldRecordTheFactsAsResearchOnTheTopic() => document.ShouldNotBeNull().ShouldSatisfyAllConditions(
            value => value.Origin.ShouldBe(FactOrigin.Research),
            value => value.Topic.ShouldBe(ResearchTopic),
            value => value.SourceNote.ShouldBe(NoteName));

        void ItShouldKeepTheSourcesOfEachObservation() => document.ShouldNotBeNull()
            .Facts.Single().Observations.Single().Sources.ShouldBe([ResearchSource]);

        void ItShouldNotDenyTheOrganisationsItNames() => denyList.Entries.ShouldBeEmpty();
    }

    [Test]
    public async Task AnAuthorNoteIsCaptured()
    {
        _wiki.FactFinder.Behaviour = async (_, cancellationToken) => await _wiki.CallToolAsync(
            nameof(FactTools.FactWriterTool),
            Collection,
            () => _wiki.Get<FactTools>().FactWriterTool(AgentFrameworkFacts(ResearchSource, SubjectKind.Person), Collection, cancellationToken));
        await StartAsync();

        await CaptureAsync(NoteName, NoteContent);
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).Count == 1 && await _wiki.RunsFinishedAsync(2));
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        FactDocument? document = settled ? await ReadProcessedFactsAsync() : null;
        DenyList denyList = await ReadDenyListAsync();

        ItShouldFinish();
        ItShouldNotTellTheFactFinderItIsResearch();
        ItShouldRecordTheFactsAsTheAuthors();
        ItShouldDropTheSourcesTheFactFinderGave();
        ItShouldKeepThePersonOutOfSearchQueries();

        void ItShouldFinish() => settled.ShouldBeTrue(pipeline);

        void ItShouldNotTellTheFactFinderItIsResearch() =>
            _wiki.FactFinder.Prompts.ShouldHaveSingleItem().ShouldNotContain("research note");

        void ItShouldRecordTheFactsAsTheAuthors() => document.ShouldNotBeNull().ShouldSatisfyAllConditions(
            value => value.Origin.ShouldBe(FactOrigin.Author),
            value => value.Topic.ShouldBeNull(),
            value => value.Facts.Single().Subject.Kind.ShouldBe(SubjectKind.Person));

        void ItShouldDropTheSourcesTheFactFinderGave() =>
            document.ShouldNotBeNull().Facts.Single().Observations.Single().Sources.ShouldBeNull();

        void ItShouldKeepThePersonOutOfSearchQueries() => denyList.Entries.ShouldHaveSingleItem()
            .ShouldBe(new DenyListEntry("Agent Framework", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied));
    }

    [Test]
    public async Task AResearchNoteTriesToRewriteTheWiki()
    {
        _wiki.FactFinder.Behaviour = async (_, cancellationToken) => await _wiki.CallToolAsync(
            nameof(FactTools.FactWriterTool),
            Collection,
            () => _wiki.Get<FactTools>().FactWriterTool(AgentFrameworkFacts(ResearchSource, SubjectKind.Organisation), Collection, cancellationToken));
        _wiki.ThreadWeaver.Behaviour = TryToRewriteTheWikiAsync;
        await StartAsync();
        WriteWikiPage(ProjectPage, ProjectPageContent);

        await CaptureAsync(NoteName, ResearchNote);
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).Count == 1 && await _wiki.RunsFinishedAsync(2));
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        AgentRun threadWeaver = (await _wiki.RunsAsync()).Single(run => run.Agent == KnownAgents.ThreadWeaver);

        ItShouldFinish();
        ItShouldRecordTheRunAsResearch();
        ItShouldRefuseToDeleteOrMovePages();
        ItShouldRefuseToWriteIntoPersonalSections();
        ItShouldLetTheResearchBeAddedToItsTopic();
        ItShouldLeaveTheWikiAsItWas();

        void ItShouldFinish() => settled.ShouldBeTrue(pipeline);

        void ItShouldRecordTheRunAsResearch() => threadWeaver.ShouldSatisfyAllConditions(
            run => run.Origin.ShouldBe(FactOrigin.Research),
            run => run.Status.ShouldBe(AgentRunStatus.Succeeded));

        void ItShouldRefuseToDeleteOrMovePages()
        {
            AgentToolCall[] removals = threadWeaver.ToolCalls
                .Where(call => call.Tool is nameof(FileTools.DeleteFile) or nameof(FileTools.MoveFile))
                .ToArray();
            removals.Select(call => call.Tool).ShouldBe([nameof(FileTools.MoveFile), nameof(FileTools.DeleteFile)]);
            removals.ShouldAllBe(call => !call.Succeeded
                && call.Error!.Contains("never a reason to remove, move or restructure existing pages"));
        }

        void ItShouldRefuseToWriteIntoPersonalSections() => threadWeaver.ToolCalls
            .Single(call => call.Tool == nameof(FileTools.CreateFile) && call.Target == PersonalPage)
            .ShouldSatisfyAllConditions(
                call => call.Succeeded.ShouldBeFalse(),
                call => call.Error.ShouldNotBeNull().ShouldContain("`me/` and `people/` are the author's personal sections", Case.Sensitive));

        void ItShouldLetTheResearchBeAddedToItsTopic()
        {
            threadWeaver.ToolCalls
                .Single(call => call.Tool == nameof(FileTools.CreateFile) && call.Target == ResearchPage)
                .Succeeded.ShouldBeTrue();
            File.Exists(WikiPage(ResearchPage)).ShouldBeTrue();
        }

        void ItShouldLeaveTheWikiAsItWas()
        {
            File.ReadAllText(WikiPage(ProjectPage)).ShouldBe(ProjectPageContent);
            File.Exists(WikiPage(MovedPage)).ShouldBeFalse();
            Directory.Exists(WikiPage("people/agent-framework")).ShouldBeFalse();
        }
    }

    [Test]
    public async Task AnAuthorNoteRestructuresTheWiki()
    {
        _wiki.FactFinderWritesFacts(Collection);
        _wiki.ThreadWeaver.Behaviour = TryToRewriteTheWikiAsync;
        await StartAsync();
        WriteWikiPage(ProjectPage, ProjectPageContent);

        await CaptureAsync(NoteName, NoteContent);
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).Count == 1 && await _wiki.RunsFinishedAsync(2));
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        AgentRun threadWeaver = (await _wiki.RunsAsync()).Single(run => run.Agent == KnownAgents.ThreadWeaver);

        ItShouldFinish();
        ItShouldRecordTheRunAsTheAuthors();
        ItShouldAllowEveryChange();

        void ItShouldFinish() => settled.ShouldBeTrue(pipeline);

        void ItShouldRecordTheRunAsTheAuthors() => threadWeaver.Origin.ShouldBe(FactOrigin.Author);

        void ItShouldAllowEveryChange()
        {
            threadWeaver.ToolCalls.Select(call => (call.Tool, call.Succeeded)).ShouldBe(
            [
                (nameof(FileTools.MoveFile), true),
                (nameof(FileTools.CreateFile), true),
                (nameof(FileTools.CreateFile), true),
                (nameof(FileTools.DeleteFile), true)
            ]);
            File.Exists(WikiPage(PersonalPage)).ShouldBeTrue();
            File.Exists(WikiPage(MovedPage)).ShouldBeFalse();
        }
    }

    [Test]
    public async Task AResearchNoteHasAMalformedHeader()
    {
        _wiki.FactFinderWritesFacts(Collection);
        await StartAsync();

        await CaptureAsync(NoteName, ResearchNote.Replace($"topic: {ResearchTopic}\n", string.Empty, StringComparison.Ordinal));
        bool settled = await Eventually.Until(() => TestWiki.FileNames(_wiki.Settings.FailedNotesDirectory).Count == 1);
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        IReadOnlyList<AgentRun> runs = await _wiki.RunsAsync();

        ItShouldParkTheNoteAsFailed();
        ItShouldNotRunTheFactFinder();

        void ItShouldParkTheNoteAsFailed()
        {
            settled.ShouldBeTrue(pipeline);
            TestWiki.FileNames(_wiki.Settings.FailedNotesDirectory).ShouldBe([NoteName]);
            TestWiki.FileNames(_wiki.Settings.ProcessingNotesDirectory).ShouldBeEmpty();
        }

        void ItShouldNotRunTheFactFinder()
        {
            _wiki.FactFinder.Prompts.ShouldBeEmpty();
            runs.ShouldBeEmpty();
        }
    }

    [Test]
    [Arguments("https://example.com/made-up", SubjectKind.Other, "'https://example.com/made-up' of observation")]
    [Arguments(null, SubjectKind.Other, "has no `sources`")]
    [Arguments(ResearchSource, null, "has no `kind`")]
    public async Task TheFactFinderWritesResearchFactsTheToolRefuses(string? source, SubjectKind? kind, string problem)
    {
        _wiki.FactFinder.Behaviour = async (_, cancellationToken) => await _wiki.CallToolAsync(
            nameof(FactTools.FactWriterTool),
            Collection,
            () => _wiki.Get<FactTools>().FactWriterTool(AgentFrameworkFacts(source, kind), Collection, cancellationToken));
        await StartAsync();

        await CaptureAsync(NoteName, ResearchNote);
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.FailedNotesDirectory).Count == 1 && await _wiki.RunsFinishedAsync(1));
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        IReadOnlyList<AgentRun> runs = await _wiki.RunsAsync();

        ItShouldParkTheNoteAsFailed();
        ItShouldNotWriteTheFacts();
        ItShouldTellTheFactFinderWhatToCorrect();

        void ItShouldParkTheNoteAsFailed()
        {
            settled.ShouldBeTrue(pipeline);
            TestWiki.FileNames(_wiki.Settings.FailedNotesDirectory).ShouldBe([NoteName]);
        }

        void ItShouldNotWriteTheFacts() => TestWiki.FileNames(_wiki.Settings.FactsDirectory).ShouldBeEmpty();

        void ItShouldTellTheFactFinderWhatToCorrect() => runs.ShouldHaveSingleItem()
            .ToolCalls.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                call => call.Succeeded.ShouldBeFalse(),
                call => call.Error.ShouldNotBeNull().ShouldContain(problem, Case.Sensitive));
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
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();

        ItShouldStartWithOneNote();
        ItShouldProcessOneNoteAtATime();
        ItShouldProcessBothNotes();

        void ItShouldStartWithOneNote() => firstNoteStarted.ShouldBeTrue();

        void ItShouldProcessOneNoteAtATime() => notesReadWhileTheFirstWasProcessing.ShouldBe(1);

        void ItShouldProcessBothNotes()
        {
            settled.ShouldBeTrue(pipeline);
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).ShouldBe([NoteName, secondNote]);
        }
    }

    private static Fact[] AgentFrameworkFacts(string? source, SubjectKind? kind = SubjectKind.Other) =>
    [
        new(
            new FactSubject("Agent Framework", "A library for building agents.", kind),
            [new Observation("Version 2 was released", "The release adds workflows.", 2, [], source is null ? [] : [source])])
    ];

    /// <summary>
    /// What a ThreadWeaver led astray by injected content might do: move and delete existing pages, file the research
    /// under a person, and add it to its topic. Like a model, it reads a refused call's error and carries on.
    /// </summary>
    private async Task<string> TryToRewriteTheWikiAsync(string prompt, CancellationToken cancellationToken)
    {
        FileTools tools = _wiki.Get<FileTools>();
        await AttemptAsync(nameof(FileTools.MoveFile), $"{ProjectPage} → {MovedPage}", () =>
            tools.MoveFile(ProjectPage, MovedPage));
        await AttemptAsync(nameof(FileTools.CreateFile), PersonalPage, () =>
            tools.CreateFile(PersonalPage, "# Agent Framework\n", cancellationToken: cancellationToken));
        await AttemptAsync(nameof(FileTools.CreateFile), ResearchPage, () =>
            tools.CreateFile(ResearchPage, $"# Agent Framework\n\nAccording to [the release notes]({ResearchSource}), version 2 adds workflows.\n", cancellationToken: cancellationToken));
        string remaining = File.Exists(WikiPage(ProjectPage)) ? ProjectPage : MovedPage;
        await AttemptAsync(nameof(FileTools.DeleteFile), remaining, () => tools.DeleteFile(remaining));
        return "Filed the research.";
    }

    private async Task AttemptAsync<T>(string tool, string target, Func<Task<T>> call)
    {
        try
        {
            await _wiki.CallToolAsync(tool, target, call);
        }
        catch (InvalidOperationException)
        {
            // The refusal is recorded on the run; the agent continues with what it may do.
        }
    }

    private string WikiPage(string path) => Path.Combine(_wiki.Settings.WikiDirectory, path);

    private void WriteWikiPage(string path, string content)
    {
        string fullPath = WikiPage(path);
        _wiki.Write(Path.GetDirectoryName(fullPath)!, Path.GetFileName(fullPath), content);
    }

    private async Task<FactDocument?> ReadProcessedFactsAsync() => JsonSerializer.Deserialize<FactDocument>(
        await File.ReadAllTextAsync(Path.Combine(_wiki.Settings.ProcessedFactsDirectory, $"{Collection}.json")),
        JsonSerializerOptions.Web);

    private async Task<DenyList> ReadDenyListAsync() =>
        (await _wiki.Get<IResearchStore>().ReadDenyListAsync(CancellationToken.None)).Value;

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