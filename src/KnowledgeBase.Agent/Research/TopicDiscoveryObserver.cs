using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Research;

/// <summary>
/// Keeps the research topics in step with the wiki as wiki-writing runs end. It runs under the caller's
/// <see cref="Wiki.WikiLock"/>, so the pages it checks are not changing; it only touches research state and never
/// acquires the lock.
/// </summary>
internal sealed class TopicDiscoveryObserver(
    ILogger<TopicDiscoveryObserver> logger,
    RecordTopicChangesCommandHandler handler) : IAgentRunObserver
{
    public async Task OnRunCompletedAsync(AgentRun run, CancellationToken cancellationToken) => await handler
        .Handle(new RecordTopicChangesCommand(run), cancellationToken)
        .TapError(error => logger.FailedToRecordTopicChanges(run.Agent, run.Id, error));
}
