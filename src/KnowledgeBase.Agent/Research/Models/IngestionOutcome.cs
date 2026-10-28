using KnowledgeBase.Agent.Files.Tools;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Research.Models;

/// <summary>What became of a research note once the pipeline was done with it.</summary>
public enum IngestionOutcome
{
    /// <summary>The note is still in the pipeline.</summary>
    Waiting,

    /// <summary>The ThreadWeaver filed something from the note.</summary>
    Positive,

    /// <summary>The note was processed but nothing was filed from it: it was all noise.</summary>
    Empty,

    /// <summary>The note ended in the failed notes folder, or vanished.</summary>
    Failed
}

internal static class IngestionOutcomes
{
    private static readonly HashSet<string> FilingTools = [nameof(FileTools.CreateFile), nameof(FileTools.PatchFile)];

    /// <summary>
    /// Classifies a note by where it is and what the ThreadWeaver runs that ingested it did. A run files something when
    /// it wrote a page, which is what separates a note that taught the wiki something from one it only read.
    /// </summary>
    /// <param name="runs">The ThreadWeaver runs, of which only those that ingested <paramref name="noteFile"/> count.</param>
    public static IngestionOutcome Classify(NoteLocation location, string noteFile, IEnumerable<AgentRun> runs) => location switch
    {
        NoteLocation.New or NoteLocation.Processing => IngestionOutcome.Waiting,
        NoteLocation.Processed => runs.Any(run => Filed(run, noteFile)) ? IngestionOutcome.Positive : IngestionOutcome.Empty,
        _ => IngestionOutcome.Failed
    };

    private static bool Filed(AgentRun run, string noteFile) =>
        run.Status == AgentRunStatus.Succeeded
        && string.Equals(run.SourceNote is null ? null : Path.GetFileName(run.SourceNote), noteFile, StringComparison.OrdinalIgnoreCase)
        && run.ToolCalls.Any(call => call.Succeeded && FilingTools.Contains(call.Tool));
}
