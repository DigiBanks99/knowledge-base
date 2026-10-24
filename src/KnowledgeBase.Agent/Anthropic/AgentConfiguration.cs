using Anthropic;

using KnowledgeBase.Agent.Runs;
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
                AgentRunTracker runTracker = sp.GetRequiredService<AgentRunTracker>();
                return client
                    .AsAIAgent(
                        options.Model,
                        instructions,
                        agentName,
                        tools: [.. tools.Tools, AIFunctionFactory.Create(dateTools.GetCurrentDateTimeIso)])
                    .AsBuilder()
                    .Use((_, context, next, cancellationToken) => RecordToolCall(runTracker, context, next, cancellationToken))
                    .Build();
            });

            return toolsBuilder;
        }
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