using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Runs;

/// <summary>Where agent runs are kept, so the run tracker can be tested without the disk.</summary>
internal interface IAgentRunStore
{
    Task SaveAsync(AgentRun run, CancellationToken cancellationToken);

    Task<AgentRun?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>The runs matching <paramref name="filter"/>, newest first, skipping the newest <paramref name="offset"/>.</summary>
    Task<IReadOnlyList<AgentRun>> ListAsync(AgentRunFilter filter, int offset, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Runs still marked as running when the app starts were cut off by a restart or crash.
    /// </summary>
    Task FailInterruptedRunsAsync(CancellationToken cancellationToken);
}
