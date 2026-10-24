using System.ClientModel;

using Anthropic;

using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Wiki.Tools;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

using OpenAI;
using OpenAI.Chat;

namespace KnowledgeBase.Agent.Agents;

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
            AgentModelOptions options = builder.Configuration.GetSection(agentName).Get<AgentModelOptions>()
                ?? throw new InvalidOperationException($"Failed to bind model options for agent {agentName}. Please ensure the configuration is correct.");
            if (string.IsNullOrWhiteSpace(options.Instructions))
            {
                throw new InvalidOperationException(
                    $"Agent {agentName} has no instructions. Add config/{agentName}.Instructions.md, or run the AppHost's scaffold-config command (see the README).");
            }

            string instructions = AgentInstructions.Compose(
                builder.Configuration,
                agentName,
                options.Instructions,
                includeWikiRules);

            Func<AITool[], AIAgent> createAgent = options.Provider switch
            {
                ModelProvider.Anthropic => CreateAnthropicAgent(agentName, options, instructions),
                ModelProvider.OpenAICompatible => CreateOpenAICompatibleAgent(builder.Configuration, agentName, options, instructions),
                _ => throw new InvalidOperationException($"Agent {agentName} has an unknown model provider '{options.Provider}'.")
            };

            AIAgentToolsBuilder toolsBuilder = new(builder, agentName, []);
            builder.Services.AddKeyedSingleton<AIAgent>(agentName, (sp, _) =>
            {
                AgentToolCollection tools = sp.GetRequiredKeyedService<AgentToolCollection>(agentName);
                DateTools dateTools = sp.GetRequiredService<DateTools>();
                AgentRunTracker runTracker = sp.GetRequiredService<AgentRunTracker>();
                return createAgent([.. tools.Tools, AIFunctionFactory.Create(dateTools.GetCurrentDateTimeIso)])
                    .AsBuilder()
                    .Use((_, context, next, cancellationToken) => RecordToolCall(runTracker, context, next, cancellationToken))
                    .Build();
            });

            return toolsBuilder;
        }
    }

    private static Func<AITool[], AIAgent> CreateAnthropicAgent(
        string agentName,
        AgentModelOptions options,
        string instructions)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException($"Agent {agentName} uses the Anthropic provider and requires '{agentName}:ApiKey'.");
        }

        AnthropicClient client = new()
        {
            ApiKey = options.ApiKey,
        };

        return tools => client.AsAIAgent(options.Model, instructions, agentName, tools: tools);
    }

    private static Func<AITool[], AIAgent> CreateOpenAICompatibleAgent(
        IConfiguration configuration,
        string agentName,
        AgentModelOptions options,
        string instructions)
    {
        OpenAICompatibleOptions server = configuration
            .GetSection(OpenAICompatibleOptions.SectionName)
            .Get<OpenAICompatibleOptions>() ?? new OpenAICompatibleOptions();
        if (server.Endpoint is null)
        {
            throw new InvalidOperationException(
                $"Agent {agentName} uses the OpenAICompatible provider and requires '{OpenAICompatibleOptions.SectionName}:Endpoint'.");
        }

        // The client refuses to send a request without a key, but a server started without one accepts any.
        ApiKeyCredential credential = new(string.IsNullOrWhiteSpace(server.ApiKey) ? "unused" : server.ApiKey);
        OpenAIClient client = new(credential, new OpenAIClientOptions
        {
            Endpoint = server.Endpoint,
            NetworkTimeout = server.NetworkTimeout,
        });
        ChatClient chatClient = client.GetChatClient(options.Model);

        return tools => chatClient.AsAIAgent(instructions, agentName, tools: tools);
    }

    private static async ValueTask<object?> RecordToolCall(
        AgentRunTracker runTracker,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        string? target = ToolCallTarget(context.Arguments);
        try
        {
            object? result = await next(context, cancellationToken);
            await runTracker.RecordToolCallAsync(context.Function.Name, target, null);
            return result;
        }
        catch (Exception ex)
        {
            await runTracker.RecordToolCallAsync(context.Function.Name, target, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// The paths (or collection name) a tool call acted on, e.g. <c>people/index.md</c> or <c>a.md → b.md</c>.
    /// </summary>
    private static string? ToolCallTarget(AIFunctionArguments arguments)
    {
        string[] targets = arguments
            .Where(argument => argument.Key.Contains("path", StringComparison.OrdinalIgnoreCase)
                || argument.Key.Equals("collectionName", StringComparison.OrdinalIgnoreCase))
            .Select(argument => argument.Value?.ToString())
            .OfType<string>()
            .ToArray();
        return targets.Length == 0 ? null : string.Join(" → ", targets);
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