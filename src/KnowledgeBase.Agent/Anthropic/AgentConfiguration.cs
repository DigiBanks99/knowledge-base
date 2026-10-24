using Anthropic;

using KnowledgeBase.Agent.Wiki.Tools;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace KnowledgeBase.Agent.Anthropic;

internal sealed class AgentToolCollection
{
    public AITool[] Tools { get; private set; } = [];

    public void AddTool(AIFunction tool)
    {
        Tools = [.. Tools, tool];
    }
}

internal sealed class AIAgentToolsBuilder(
    IHostApplicationBuilder hostBuilder,
    string agentName,
    AIFunction[] tools)
{
    public IHostApplicationBuilder HostBuilder { get; } = hostBuilder;
    public AITool[] Tools { get; private set; } = tools.OfType<AITool>().ToArray();
    public string AgentName { get; } = agentName;

    public void AddTool(AIFunction tool)
    {
        Tools = [.. Tools, tool];
    }
}

internal static class AgentConfiguration
{
    extension(IHostApplicationBuilder builder)
    {
        internal AIAgentToolsBuilder ConfigureAgent(string agentName, bool includeWikiRules = false)
        {
            AnthropicOptions options = builder.Configuration.GetSection(agentName).Get<AnthropicOptions>()
                ?? throw new InvalidOperationException($"Failed to bind Anthropic options for agent {agentName}. Please ensure the configuration is correct.");
            string instructions = AgentInstructions.Compose(
                builder.Configuration,
                agentName,
                options.Instructions,
                includeWikiRules);

            AnthropicClient client = new()
            {
                ApiKey = options.ApiKey,
            };

            AIAgentToolsBuilder toolsBuilder = new(builder, agentName, []);
            builder.Services.AddKeyedSingleton<AIAgent>(agentName, (sp, _) =>
            {
                AgentToolCollection tools = sp.GetRequiredKeyedService<AgentToolCollection>(agentName);
                DateTools dateTools = sp.GetRequiredService<DateTools>();
                return client
                    .AsAIAgent(
                        options.Model,
                        instructions,
                        agentName,
                        tools: [.. tools.Tools, AIFunctionFactory.Create(dateTools.GetCurrentDateTimeIso)]);
            });

            return toolsBuilder;
        }
    }

    extension(AIAgentToolsBuilder builder)
    {
        internal IHostApplicationBuilder WithTools(Action<AIAgentToolsBuilder> configureTools)
        {
            configureTools(builder);
            return builder.HostBuilder;
        }
    }
}