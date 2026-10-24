using System.Text.Json;

using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Pipeline;

/// <summary>
/// What the app finds on disk when it starts after being stopped part-way through, and how it recovers.
/// </summary>
public sealed class RestartRecoveryTests : IAsyncDisposable
{
    private const string NoteName = "note-2026-10-24-101500.md";
    private const string NoteContent = "Susan gave feedback on the design review.";
    private const string Collection = "feedback-from-susan";

    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task TheAppStoppedWhileANoteWasBeingRead()
    {
        _wiki.FactFinderWritesFacts(Collection);
        await _wiki.InitialiseAsync();
        _wiki.Write(_wiki.Settings.ProcessingNotesDirectory, NoteName, NoteContent);

        _wiki.Start();
        bool settled = await Eventually.Until(() =>
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).Count == 1);
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();

        ItShouldReadTheNoteAgain();
        ItShouldFinishProcessingIt();

        void ItShouldReadTheNoteAgain() => _wiki.FactFinder.Prompts.ShouldHaveSingleItem().ShouldContain(NoteContent);

        void ItShouldFinishProcessingIt()
        {
            settled.ShouldBeTrue(pipeline);
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).ShouldBe([NoteName]);
        }
    }

    [Test]
    public async Task TheAppStoppedWhileFactsWereWaiting()
    {
        await _wiki.InitialiseAsync();
        _wiki.Write(_wiki.Settings.ProcessingNotesDirectory, NoteName, NoteContent);
        _wiki.Write(
            _wiki.Settings.FactsDirectory,
            $"{Collection}.json",
            JsonSerializer.Serialize(new FactDocument(NoteName, AgentScripts.SampleFacts), JsonSerializerOptions.Web));

        _wiki.Start();
        bool settled = await Eventually.Until(() =>
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).Count == 1
            && TestWiki.FileNames(_wiki.Settings.ProcessedFactsDirectory).Count == 1);
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();

        ItShouldNotReadTheNoteAgain();
        ItShouldWeaveTheWaitingFacts();
        ItShouldCompleteTheNote();

        void ItShouldNotReadTheNoteAgain() => _wiki.FactFinder.Prompts.ShouldBeEmpty();

        void ItShouldWeaveTheWaitingFacts() => _wiki.ThreadWeaver.Prompts.ShouldHaveSingleItem().ShouldContain("Susan");

        void ItShouldCompleteTheNote()
        {
            settled.ShouldBeTrue(pipeline);
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).ShouldBe([NoteName]);
        }
    }

    [Test]
    public async Task TheAppStoppedMidRun()
    {
        await _wiki.InitialiseAsync();
        AgentRunStore store = _wiki.Get<AgentRunStore>();
        AgentRun interrupted = Run("20261024-101500-factfinder-interrupted", AgentRunStatus.Running, completedAt: null);
        AgentRun finished = Run("20261024-100000-factfinder-finished", AgentRunStatus.Succeeded, completedAt: DateTimeOffset.Parse("2026-10-24T10:01:00Z"));
        await store.SaveAsync(interrupted, CancellationToken.None);
        await store.SaveAsync(finished, CancellationToken.None);

        _wiki.Start();
        AgentRun? afterInterrupted = await store.GetAsync(interrupted.Id, CancellationToken.None);
        AgentRun? afterFinished = await store.GetAsync(finished.Id, CancellationToken.None);

        ItShouldFailTheInterruptedRun();
        ItShouldLeaveTheFinishedRunAlone();

        void ItShouldFailTheInterruptedRun() => afterInterrupted.ShouldNotBeNull().ShouldSatisfyAllConditions(
            run => run.Status.ShouldBe(AgentRunStatus.Failed),
            run => run.Error.ShouldBe("The run was interrupted because the app stopped."),
            run => run.CompletedAt.ShouldNotBeNull());

        void ItShouldLeaveTheFinishedRunAlone() => afterFinished.ShouldNotBeNull().ShouldSatisfyAllConditions(
            run => run.Status.ShouldBe(AgentRunStatus.Succeeded),
            run => run.CompletedAt.ShouldBe(finished.CompletedAt));
    }

    private static AgentRun Run(string id, AgentRunStatus status, DateTimeOffset? completedAt) => new(
        id,
        KnownAgents.FactFinder,
        new AgentRunTrigger(AgentRunTriggerKind.Note, NoteName),
        status,
        DateTimeOffset.Parse("2026-10-24T10:00:00Z"),
        completedAt,
        null,
        null,
        []);
}