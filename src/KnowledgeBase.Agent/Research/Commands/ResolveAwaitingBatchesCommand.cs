using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>
/// Settles the batches awaiting the ThreadWeaver: their topics' intervals follow what became of the research notes.
/// </summary>
public record struct ResolveAwaitingBatchesCommand;

/// <summary>What one resolution did.</summary>
/// <param name="Positive">Topics the ThreadWeaver filed something for, now due again after the default interval.</param>
/// <param name="Empty">Topics whose note was processed with nothing filed, now backed off.</param>
/// <param name="Failed">Topics whose note failed; their interval is unchanged.</param>
/// <param name="Pruned">Completed batches deleted for age.</param>
public sealed record ResolvedBatches(
    IReadOnlyList<string> Positive,
    IReadOnlyList<string> Empty,
    IReadOnlyList<string> Failed,
    int Pruned);

internal sealed class ResolveAwaitingBatchesCommandHandler(
    ILogger<ResolveAwaitingBatchesCommandHandler> logger,
    IResearchStore store,
    IResearchNoteWriter noteWriter,
    IAgentRunStore runStore,
    TimeProvider timeProvider)
{
    /// <summary>How long a completed batch is kept for the author to inspect.</summary>
    public static TimeSpan CompletedBatchRetention { get; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Resolves every awaiting batch whose note is out of the pipeline, and prunes old completed batches. State is
    /// derived from the notes folders and the run store alone, so a restart loses nothing: the next call resolves what
    /// the previous one left.
    /// </summary>
    public async Task<Result<ResolvedBatches, Error>> Handle(ResolveAwaitingBatchesCommand _, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AwaitingBatch>, Error> awaiting = await store.ListAwaitingBatchesAsync(cancellationToken);
        if (awaiting.IsFailure)
        {
            return awaiting.Error;
        }

        List<string> positive = [];
        List<string> empty = [];
        List<string> failed = [];
        List<Error> errors = [];
        IReadOnlyList<AgentRun>? runs = null;
        foreach (AwaitingBatch batch in awaiting.Value)
        {
            NoteLocation location = noteWriter.Locate(batch.NoteFile);
            // The run store is only read once, and only when a note has been processed and its runs matter.
            runs ??= location == NoteLocation.Processed
                ? await runStore.ListAsync(new AgentRunFilter(KnownAgents.ThreadWeaver, AgentRunTriggerKind.Facts), 0, int.MaxValue, cancellationToken)
                : null;
            IngestionOutcome outcome = IngestionOutcomes.Classify(location, batch.NoteFile, runs ?? []);
            if (outcome == IngestionOutcome.Waiting)
            {
                continue;
            }

            Result<Maybe<ResearchBatch>, Error> resolved = await ResolveAsync(batch, outcome, cancellationToken);
            if (resolved.IsFailure)
            {
                errors.Add(resolved.Error);
                continue;
            }

            (outcome switch
            {
                IngestionOutcome.Positive => positive,
                IngestionOutcome.Empty => empty,
                _ => failed
            }).Add(batch.Batch.Topic);
        }

        Result<int, Error> pruned = await store.PruneCompletedBatchesAsync(
            timeProvider.GetUtcNow() - CompletedBatchRetention,
            cancellationToken);
        if (pruned.IsFailure)
        {
            errors.Add(pruned.Error);
        }
        else if (pruned.Value > 0)
        {
            logger.CompletedBatchesPruned(pruned.Value);
        }

        return errors.Count switch
        {
            0 => new ResolvedBatches(positive, empty, failed, pruned.Value),
            1 => errors[0],
            _ => new AggregateError(errors)
        };
    }

    /// <summary>
    /// Applies the outcome to the topic before completing the batch, so a failure in between leaves the batch awaiting
    /// and the next call resolves it again; the interval then moves once more, which is the safer way to be wrong.
    /// </summary>
    private async Task<Result<Maybe<ResearchBatch>, Error>> ResolveAsync(
        AwaitingBatch batch,
        IngestionOutcome outcome,
        CancellationToken cancellationToken)
    {
        string topic = batch.Batch.Topic;
        DateTimeOffset now = timeProvider.GetUtcNow();
        return await store
            .UpdateTopicAsync(
                topic,
                // A topic pruned meanwhile has no state left to move; its batch still completes.
                current => Result.Success<Maybe<ResearchTopic>, Error>(current.Select(existing => outcome switch
                {
                    IngestionOutcome.Positive => existing.Positive(now),
                    IngestionOutcome.Empty => existing.Empty(),
                    _ => existing.Failed()
                })),
                cancellationToken)
            .Tap(updated => logger.BatchResolved(topic, outcome.ToString(), updated.HasValue && updated.Value.IsFailing))
            .Bind(_ => store.CompleteAwaitingBatchAsync(topic, cancellationToken));
    }
}
