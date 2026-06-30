using Anthropic;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace KnowledgeBase.Agent.Anthropic;

internal sealed class AgentToolCollection
{
    public AIFunction[] Tools { get; private set; } = [];

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
    public AIFunction[] Tools { get; private set; } = tools;
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
        internal AIAgentToolsBuilder ConfigureAgent(string agentName)
        {
            IConfiguration config = builder.Configuration.GetSection(agentName);
            AnthropicOptions options = config.Get<AnthropicOptions>()
                ?? throw new InvalidOperationException($"Failed to bind Anthropic options for agent {agentName}. Please ensure the configuration is correct.");

            AnthropicClient client = new()
            {
                ApiKey = options.ApiKey,
            };


            AIAgentToolsBuilder toolsBuilder = new(builder, agentName, []);
            builder.Services.AddKeyedSingleton<AIAgent>(agentName, (sp, _) =>
            {
                AgentToolCollection tools = sp.GetRequiredKeyedService<AgentToolCollection>(agentName);
                return client
                    .AsAIAgent(
                        options.Model,
                        agentName,
                        options.Instructions,
                        tools: tools.Tools);
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