using KnowledgeBase.Agent.Common;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Runs;

internal sealed class AgentRunStore(IOptions<WikiConfigurationSettings> options, TimeProvider timeProvider)
{
    private readonly WikiConfigurationSettings _settings = options.Value;

    public Task SaveAsync(AgentRun run, CancellationToken cancellationToken) =>
        JsonFileStore.WriteAsync(RunPath(run.Id), run, cancellationToken);

    public Task<AgentRun?> GetAsync(string id, CancellationToken cancellationToken) =>
        IsValidId(id)
            ? JsonFileStore.ReadAsync<AgentRun>(RunPath(id), cancellationToken)
            : Task.FromResult<AgentRun?>(null);

    public async Task<IReadOnlyList<AgentRun>> ListAsync(
        string? agent,
        AgentRunTriggerKind? triggerKind,
        string? triggerReference,
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentRun> runs = await JsonFileStore.ReadAllAsync<AgentRun>(_settings.RunsDirectory, cancellationToken);
        return runs
            .Where(run => agent is null || run.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase))
            .Where(run => triggerKind is null || run.Trigger.Kind == triggerKind)
            .Where(run => triggerReference is null || run.Trigger.Reference.Equals(triggerReference, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => run.StartedAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Runs still marked as running when the app starts were cut off by a restart or crash.
    /// </summary>
    public async Task FailInterruptedRunsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentRun> runs = await JsonFileStore.ReadAllAsync<AgentRun>(_settings.RunsDirectory, cancellationToken);
        foreach (AgentRun run in runs.Where(run => run.Status == AgentRunStatus.Running))
        {
            await SaveAsync(
                run with
                {
                    Status = AgentRunStatus.Failed,
                    CompletedAt = timeProvider.GetUtcNow(),
                    Error = "The run was interrupted because the app stopped."
                },
                cancellationToken);
        }
    }

    private string RunPath(string id) => Path.Combine(_settings.RunsDirectory, $"{id}.json");

    private static bool IsValidId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}