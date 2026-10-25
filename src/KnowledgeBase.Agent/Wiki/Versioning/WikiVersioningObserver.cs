using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Wiki.Versioning;

/// <summary>
/// Commits the wiki when a wiki-writing agent's run ends, whether it succeeded or failed, so a run's changes can be
/// inspected or reverted on their own.
/// </summary>
/// <remarks>
/// ThreadWeaver and Editor runs execute while their caller holds <see cref="WikiLock"/>, and observers are called
/// before the run returns, so the commit is taken under that lock and never captures a page another run is halfway
/// through. The lock cannot be re-entered: this observer relies on the caller's hold and must never acquire it.
/// A new commit is then pushed to the backup remote, when one is configured, under the same lock. A failed commit or
/// push is logged and never affects the run.
/// </remarks>
internal sealed class WikiVersioningObserver(
    ILogger<WikiVersioningObserver> logger,
    CommitWikiRunCommandHandler commitHandler,
    PushWikiCommandHandler pushHandler) : IAgentRunObserver
{
    private static readonly HashSet<string> WikiWritingAgents = [KnownAgents.ThreadWeaver, KnownAgents.Editor];

    public async Task OnRunCompletedAsync(AgentRun run, CancellationToken cancellationToken)
    {
        if (!WikiWritingAgents.Contains(run.Agent))
        {
            return;
        }

        await commitHandler
            .Handle(new CommitWikiRunCommand(run), cancellationToken)
            .Tap(committed =>
            {
                if (committed)
                {
                    logger.CommittedWikiRun(run.Agent, run.Id);
                }
                else
                {
                    logger.NothingToCommitForWikiRun(run.Agent, run.Id);
                }
            })
            .TapError(error => logger.FailedToCommitWikiRun(run.Agent, run.Id, error))
            .Tap(async committed =>
            {
                if (committed)
                {
                    await pushHandler.Handle(new PushWikiCommand(), cancellationToken);
                }
            });
    }
}