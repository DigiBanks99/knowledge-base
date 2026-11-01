using KnowledgeBase.Agent.Agents;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Status;

/// <summary>Whether an agent's model can take work right now, for background work that should wait out an outage.</summary>
internal interface IModelServerHealth
{
    /// <summary>
    /// Whether the model server the agent uses is healthy. An agent on another provider never waits for it, since a
    /// local server being down says nothing about that provider.
    /// </summary>
    Task<bool> IsAvailableToAsync(string agent, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the research Keeper's server is healthy. On the shared model server that is the server's own signal; a
    /// server of the Keeper's own is not checked, and a call it fails simply waits for the next cycle.
    /// </summary>
    Task<bool> IsKeeperServerAvailableAsync(CancellationToken cancellationToken);
}

/// <summary>Runs the model server's status signal on its own, so the answer is current rather than up to a period old.</summary>
internal sealed class ModelServerHealth(HealthCheckService healthChecks, IConfiguration configuration) : IModelServerHealth
{
    public async Task<bool> IsAvailableToAsync(string agent, CancellationToken cancellationToken)
    {
        bool usesModelServer = string.Equals(
            configuration[$"{agent}:{nameof(AgentModelOptions.Provider)}"],
            nameof(ModelProvider.OpenAICompatible),
            StringComparison.OrdinalIgnoreCase);
        return !usesModelServer || await IsHealthyAsync(cancellationToken);
    }

    public async Task<bool> IsKeeperServerAvailableAsync(CancellationToken cancellationToken) =>
        !ModelServerHealthCheck.UsesSharedServerForKeeper(configuration) || await IsHealthyAsync(cancellationToken);

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        HealthReport report = await healthChecks.CheckHealthAsync(
            registration => registration.Name == ModelServerHealthCheck.Name,
            cancellationToken);
        return report.Status == HealthStatus.Healthy;
    }
}
