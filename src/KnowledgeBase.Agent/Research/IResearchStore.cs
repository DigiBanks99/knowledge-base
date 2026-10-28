using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research;

/// <summary>The research state kept under the <c>research/</c> directory of the wiki root.</summary>
internal interface IResearchStore
{
    /// <summary>The deny-list, or an empty one when none has been written yet.</summary>
    Task<Result<DenyList, Error>> ReadDenyListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads, changes and writes the deny-list as one step, so concurrent changes (a fact document being recorded while
    /// the author toggles an entry) never lose each other. Nothing is written when <paramref name="update"/> fails.
    /// </summary>
    Task<Result<DenyList, Error>> UpdateDenyListAsync(
        Func<DenyList, Result<DenyList, Error>> update,
        CancellationToken cancellationToken);

    /// <summary>Every topic with research state, skipping any whose file cannot be read.</summary>
    Task<Result<IReadOnlyList<ResearchTopic>, Error>> ListTopicsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads, changes and writes a topic's state as one step. <paramref name="update"/> receives the current state
    /// (none for an unknown topic) and returns the new one; returning none prunes the topic, including any curation
    /// request waiting for it or failed. Nothing is written when <paramref name="update"/> fails.
    /// </summary>
    /// <param name="topic">The topic's wiki path, such as <c>projects/foo</c>.</param>
    Task<Result<Maybe<ResearchTopic>, Error>> UpdateTopicAsync(
        string topic,
        Func<Maybe<ResearchTopic>, Result<Maybe<ResearchTopic>, Error>> update,
        CancellationToken cancellationToken);

    /// <summary>The curation requests waiting for the Pleb.</summary>
    Task<Result<IReadOnlyList<CurationRequest>, Error>> ListCurationRequestsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Queues a curation request for a topic, merging it into one already waiting for the same topic, so a burst of
    /// changes becomes one curation. Returns the request as it now waits.
    /// </summary>
    Task<Result<CurationRequest, Error>> RequestCurationAsync(CurationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a curated request and any earlier failure of its topic. A request that changed while it was being
    /// curated (the topic changed again) stays waiting, so the newer changes are curated too.
    /// </summary>
    Task<UnitResult<Error>> CompleteCurationAsync(CurationRequest curated, CancellationToken cancellationToken);

    /// <summary>
    /// Records a curation that failed in the failed folder and removes its request, unless the request changed while
    /// it was being curated, in which case the newer request stays waiting.
    /// </summary>
    Task<UnitResult<Error>> FailCurationAsync(FailedCuration failure, CancellationToken cancellationToken);

    /// <summary>The curations that failed, at most one per topic.</summary>
    Task<Result<IReadOnlyList<FailedCuration>, Error>> ListFailedCurationsAsync(CancellationToken cancellationToken);

    /// <summary>A topic's state, or none for an unknown topic.</summary>
    Task<Result<Maybe<ResearchTopic>, Error>> ReadTopicAsync(string topic, CancellationToken cancellationToken);

    /// <summary>
    /// The topics with a batch in flight, waiting for the Scout or awaiting ingestion, from the batch files' names, so a
    /// batch that cannot be read still counts as in flight.
    /// </summary>
    Task<Result<IReadOnlySet<string>, Error>> ListTopicsInFlightAsync(CancellationToken cancellationToken);

    /// <summary>The batches waiting for the Scout, at most one per topic, skipping any that cannot be read.</summary>
    Task<Result<IReadOnlyList<ResearchBatch>, Error>> ListPendingBatchesAsync(CancellationToken cancellationToken);

    /// <summary>The topic's batch waiting for the Scout, or none.</summary>
    Task<Result<Maybe<ResearchBatch>, Error>> ReadPendingBatchAsync(string topic, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a search's batch for the Scout, unless the topic has been pruned meanwhile, in which case nothing is
    /// written and none is returned.
    /// </summary>
    Task<Result<Maybe<ResearchBatch>, Error>> WritePendingBatchAsync(ResearchBatch batch, CancellationToken cancellationToken);

    /// <summary>
    /// Reads, changes and writes the topic's pending batch as one step, so each fetched page is kept as soon as it is
    /// fetched. None, with nothing written, when the topic has no pending batch (it was pruned meanwhile).
    /// </summary>
    Task<Result<Maybe<ResearchBatch>, Error>> UpdatePendingBatchAsync(
        string topic,
        Func<ResearchBatch, ResearchBatch> update,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves the topic's pending batch to awaiting, recording the note its kept results were written to. None, with
    /// nothing moved, when the topic has no pending batch (it was pruned meanwhile).
    /// </summary>
    Task<Result<Maybe<AwaitingBatch>, Error>> AwaitBatchAsync(
        string topic,
        string noteFile,
        DateTimeOffset notedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves the topic's pending batch to completed, for a batch with nothing kept. None, with nothing moved, when the
    /// topic has no pending batch.
    /// </summary>
    Task<Result<Maybe<ResearchBatch>, Error>> CompleteBatchAsync(string topic, CancellationToken cancellationToken);

    /// <summary>The batches awaiting the ThreadWeaver's outcome, at most one per topic, skipping any that cannot be read.</summary>
    Task<Result<IReadOnlyList<AwaitingBatch>, Error>> ListAwaitingBatchesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Moves the topic's awaiting batch to completed, once its outcome is known. None, with nothing moved, when the topic
    /// has no awaiting batch.
    /// </summary>
    Task<Result<Maybe<ResearchBatch>, Error>> CompleteAwaitingBatchAsync(string topic, CancellationToken cancellationToken);

    /// <summary>Deletes the completed batches written before <paramref name="olderThan"/>, and says how many.</summary>
    Task<Result<int, Error>> PruneCompletedBatchesAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);

    /// <summary>The domains the author blocked, or none when none have been written yet.</summary>
    Task<Result<BlockedDomains, Error>> ReadBlockedDomainsAsync(CancellationToken cancellationToken);
}
