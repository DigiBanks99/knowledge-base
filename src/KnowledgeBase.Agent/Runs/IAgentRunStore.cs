using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Runs;

/// <summary>Where agent runs are kept, so the run tracker can be tested without the disk.</summary>
internal interface IAgentRunStore
{
    Task SaveAsync(AgentRun run, CancellationToken cancellationToken);

    Task<AgentRun?> GetAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentRun>> ListAsync(
        string? agent,
        AgentRunTriggerKind? triggerKind,
        string? triggerReference,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs still marked as running when the app starts were cut off by a restart or crash.
    /// </summary>
    Task FailInterruptedRunsAsync(CancellationToken cancellationToken);
}
