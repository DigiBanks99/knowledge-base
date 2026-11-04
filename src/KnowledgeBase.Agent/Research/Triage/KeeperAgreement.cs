using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Triage;

/// <summary>
/// How the research loop did over a recent window, judged on production traffic: how often the Keeper's verdicts agree
/// with the author's marks, and how often the ThreadWeaver filed something from the research notes.
/// </summary>
/// <param name="KeeperModel">The Keeper's configured model, so figures from before and after a change can be told apart.</param>
/// <param name="Marked">Results the author marked that have a real verdict, judged within the window.</param>
/// <param name="Agreed">Marked results whose verdict matched the mark: kept and useful, or discarded and noise.</param>
/// <param name="NoiseKept">Results the Keeper kept that the author marked as noise.</param>
/// <param name="UsefulDiscarded">Results the Keeper discarded that the author marked as useful.</param>
/// <param name="NotesFiled">Research notes resolved within the window that the ThreadWeaver filed something from.</param>
/// <param name="NotesEmpty">Research notes processed with nothing filed.</param>
/// <param name="NotesFailed">Research notes that failed in the pipeline.</param>
public sealed record ResearchQuality(
    string KeeperModel,
    int WindowDays,
    int Marked,
    int Agreed,
    int NoiseKept,
    int UsefulDiscarded,
    int NotesFiled,
    int NotesEmpty,
    int NotesFailed);

internal static class KeeperAgreement
{
    /// <summary>Short enough that a model or threshold change shows within days.</summary>
    public static TimeSpan Window { get; } = TimeSpan.FromDays(7);

    /// <summary>
    /// The figures over the batches, counting results judged and notes resolved at or after <paramref name="since"/>.
    /// Only verdicts the Keeper itself gave count: any other says nothing about it. Batches left in two folders by a
    /// crash are counted once.
    /// </summary>
    public static ResearchQuality From(IEnumerable<ResearchBatch> batches, DateTimeOffset since, string keeperModel)
    {
        ResearchBatch[] all = [.. batches.DistinctBy(batch => (batch.Topic, batch.SearchedAt))];
        (bool Kept, ResultMark Mark)[] marked =
        [
            .. all
                .SelectMany(batch => batch.Results)
                .Where(result => result is { Mark: not null, Verdict: { ByKeeper: true } verdict } && verdict.JudgedAt >= since)
                .Select(result => (result.Verdict!.Kept, result.Mark!.Value))
        ];
        IngestionOutcome[] outcomes =
        [
            .. all
                .Where(batch => batch is { Outcome: not null, ResolvedAt: { } resolvedAt } && resolvedAt >= since)
                .Select(batch => batch.Outcome!.Value)
        ];

        return new ResearchQuality(
            keeperModel,
            Window.Days,
            marked.Length,
            marked.Count(result => result.Kept == (result.Mark == ResultMark.Useful)),
            marked.Count(result => result is { Kept: true, Mark: ResultMark.Noise }),
            marked.Count(result => result is { Kept: false, Mark: ResultMark.Useful }),
            outcomes.Count(outcome => outcome == IngestionOutcome.Positive),
            outcomes.Count(outcome => outcome == IngestionOutcome.Empty),
            outcomes.Count(outcome => outcome == IngestionOutcome.Failed));
    }
}
