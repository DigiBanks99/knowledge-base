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
}
