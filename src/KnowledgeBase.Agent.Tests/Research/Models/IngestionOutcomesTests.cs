using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Files.Tools;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Tests.Research.Models;

public sealed class IngestionOutcomesTests
{
    private const string Note = "research-projects--atlas-20261027-070000.md";
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(NoteLocation.New)]
    [Arguments(NoteLocation.Processing)]
    public void TheNoteIsStillInThePipeline(NoteLocation location)
    {
        IngestionOutcome outcome = IngestionOutcomes.Classify(location, Note, []);

        ItShouldKeepWaiting();

        void ItShouldKeepWaiting() => outcome.ShouldBe(IngestionOutcome.Waiting);
    }

    [Test]
    [Arguments(nameof(FileTools.CreateFile))]
    [Arguments(nameof(FileTools.PatchFile))]
    public void TheThreadWeaverWroteAPage(string tool)
    {
        IngestionOutcome outcome = IngestionOutcomes.Classify(NoteLocation.Processed, Note, [Run(Note, Call(tool))]);

        ItShouldBePositive();

        void ItShouldBePositive() => outcome.ShouldBe(IngestionOutcome.Positive);
    }

    [Test]
    public void TheThreadWeaverOnlyReadTheWiki()
    {
        IngestionOutcome outcome = IngestionOutcomes.Classify(
            NoteLocation.Processed,
            Note,
            [Run(Note, Call("ReadFile"), Call(nameof(FileTools.CreateFile), succeeded: false))]);

        ItShouldBeEmpty();

        void ItShouldBeEmpty() => outcome.ShouldBe(IngestionOutcome.Empty);
    }

    [Test]
    public void AnotherNotesRunWroteAPage()
    {
        IngestionOutcome outcome = IngestionOutcomes.Classify(
            NoteLocation.Processed,
            Note,
            [Run("another-note.md", Call(nameof(FileTools.CreateFile)))]);

        ItShouldNotCountForThisNote();

        void ItShouldNotCountForThisNote() => outcome.ShouldBe(IngestionOutcome.Empty);
    }

    [Test]
    [Arguments(NoteLocation.Failed)]
    [Arguments(NoteLocation.Missing)]
    public void TheNoteFailedOrVanished(NoteLocation location)
    {
        IngestionOutcome outcome = IngestionOutcomes.Classify(location, Note, [Run(Note, Call(nameof(FileTools.CreateFile)))]);

        ItShouldFail();

        void ItShouldFail() => outcome.ShouldBe(IngestionOutcome.Failed);
    }

    private static AgentToolCall Call(string tool, bool succeeded = true) =>
        new(tool, "projects/atlas/index.md", Now, succeeded, succeeded ? null : "Refused.");

    private static AgentRun Run(string sourceNote, params AgentToolCall[] calls) => new(
        "run",
        KnownAgents.ThreadWeaver,
        new AgentRunTrigger(AgentRunTriggerKind.Facts, "facts.json"),
        AgentRunStatus.Succeeded,
        Now,
        Now,
        null,
        null,
        calls,
        FactOrigin.Research,
        "projects/atlas",
        sourceNote);
}
