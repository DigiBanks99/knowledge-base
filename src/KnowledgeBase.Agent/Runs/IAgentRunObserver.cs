using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Runs;

/// <summary>
/// Reacts when an agent run ends, whether it succeeded or failed. Observers are called one at a time after the run
/// is saved, and a failing observer is logged without affecting the run or the observers after it.
/// </summary>
/// <remarks>
/// Wiki agents run while holding <see cref="Wiki.WikiLock"/>, so observers of their runs are called under it too.
/// The lock cannot be re-entered: an observer must never acquire it. An observer must not depend on
/// <see cref="AgentRunTracker"/> either; to start more agent work, queue it instead.
/// </remarks>
internal interface IAgentRunObserver
{
    Task OnRunCompletedAsync(AgentRun run, CancellationToken cancellationToken);
}