using KnowledgeBase.Agent.Agents.Models;

namespace KnowledgeBase.Agent.Agents.Queries;

public record struct ListAgentProvidersQuery;

internal sealed class ListAgentProvidersQueryHandler(IAgentProviderOverrides overrides, IConfiguration configuration)
{
    public Result<IReadOnlyList<AgentProviderSetting>, Error> Handle(ListAgentProvidersQuery _) =>
        Result.Success<IReadOnlyList<AgentProviderSetting>, Error>(
            [.. KnownAgents.All.Select(agent => Describe(overrides, configuration, agent))]);

    /// <summary>The agent's provider as the settings page shows it.</summary>
    internal static AgentProviderSetting Describe(
        IAgentProviderOverrides overrides,
        IConfiguration configuration,
        string agent)
    {
        ModelProvider defaultProvider = overrides.Defaults.GetValueOrDefault(agent, ModelProvider.Anthropic);
        return new AgentProviderSetting(
            agent,
            overrides.Overrides.GetValueOrDefault(agent, defaultProvider),
            defaultProvider,
            [
                .. AgentProviders.All.Select(provider => new ProviderChoice(
                    provider,
                    AgentProviders.Model(configuration, provider),
                    AgentProviders.Problem(configuration, provider)))
            ]);
    }
}
