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
        if (!usesModelServer)
        {
            return true;
        }

        HealthReport report = await healthChecks.CheckHealthAsync(
            registration => registration.Name == ModelServerHealthCheck.Name,
            cancellationToken);
        return report.Status == HealthStatus.Healthy;
    }
}
