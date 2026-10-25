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
}