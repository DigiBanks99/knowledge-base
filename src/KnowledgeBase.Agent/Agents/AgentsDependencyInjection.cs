using KnowledgeBase.Agent.Agents.Commands;
using KnowledgeBase.Agent.Agents.Queries;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Agents;

internal static class AgentsDependencyInjection
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Layers the providers chosen on the settings page over the configuration. Call it before the agents are
        /// configured, so each starts on its chosen provider.
        /// </summary>
        public IHostApplicationBuilder AddAgentProviders()
        {
            string rootDirectory = builder.Configuration[$"{WikiConfigurationSettings.SectionName}:{nameof(WikiConfigurationSettings.RootDirectory)}"]
                ?? throw new InvalidOperationException(
                    $"'{WikiConfigurationSettings.SectionName}:{nameof(WikiConfigurationSettings.RootDirectory)}' is not set.");
            string path = new WikiConfigurationSettings { RootDirectory = rootDirectory }.AgentProvidersPath;

            // Read before the overrides are layered on, so these are the deployment's own.
            Dictionary<string, ModelProvider> defaults = KnownAgents.All.ToDictionary(
                agent => agent,
                agent => AgentProviders.Current(builder.Configuration, agent));

            AgentProviderOverrides overrides = new(path, defaults);
            builder.Configuration.Add(overrides);
            builder.Services
                .AddSingleton<IAgentProviderOverrides>(overrides)
                .AddSingleton<ListAgentProvidersQueryHandler>()
                .AddSingleton<SetAgentProviderCommandHandler>();

            return builder;
        }
    }
}
