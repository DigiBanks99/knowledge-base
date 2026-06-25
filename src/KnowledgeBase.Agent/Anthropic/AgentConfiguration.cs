using Anthropic;

using Microsoft.Agents.AI;

namespace KnowledgeBase.Agent.Anthropic;

public sealed class AnthropicOptions
{
    public required string ApiKey { get; init; }
    public string Model { get; init; } = "claude-haiku-4.5";
    public required string Instructions { get; init; }
}

internal static class AgentConfiguration
{
    extension(IHostApplicationBuilder builder)
    {
        internal IHostApplicationBuilder ConfigureAgent(string agentName)
        {
            IConfiguration config = builder.Configuration.GetSection(agentName);
            AnthropicOptions options = config.Get<AnthropicOptions>()
                ?? throw new InvalidOperationException($"Failed to bind Anthropic options for agent {agentName}. Please ensure the configuration is correct.");
            
            AnthropicClient client = new()
            {
                ApiKey = options.ApiKey,
            };

            AIAgent agent = client.AsAIAgent(options.Model, agentName, options.Instructions);

            builder.Services.AddKeyedSingleton(agentName, agent);

            return builder;
        }
    }
}