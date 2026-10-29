using System.ClientModel;

using Anthropic;

using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Wiki.Tools;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

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
        /// <param name="roleOnly">
        /// Instructs the agent with its role alone, without the pipeline, the shared instructions or the date tool, for
        /// small local models that are given everything they need in the prompt.
        /// </param>
        internal AIAgentToolsBuilder ConfigureAgent(string agentName, bool includeWikiRules = false, bool roleOnly = false)
        {
            AgentModelOptions options = builder.Configuration.GetSection(agentName).Get<AgentModelOptions>()
                ?? throw new InvalidOperationException($"Failed to bind model options for agent {agentName}. Please ensure the configuration is correct.");
            if (string.IsNullOrWhiteSpace(options.Instructions))
            {
                throw new InvalidOperationException(
                    $"Agent {agentName} has no instructions. Add config/{agentName}.Instructions.md, or run the AppHost's scaffold-config command (see the README).");
            }

            string instructions = roleOnly
                ? AgentInstructions.ComposeRoleOnly(agentName, options.Instructions)
                : AgentInstructions.Compose(builder.Configuration, agentName, options.Instructions, includeWikiRules);

            // Only the provider the agent starts on must be usable; another is checked when it is chosen.
            string? problem = AgentProviders.Problem(builder.Configuration, options.Provider);
            if (problem is not null)
            {
                throw new InvalidOperationException($"Agent {agentName} cannot start: {problem}");
            }

            IConfiguration configuration = builder.Configuration;
            builder.Services.Configure<AgentModelOptions>(agentName, configuration.GetSection(agentName));

            AIAgentToolsBuilder toolsBuilder = new(builder, agentName, []);
            builder.Services.AddKeyedSingleton<AIAgent>(agentName, (sp, _) =>
            {
                AgentToolCollection tools = sp.GetRequiredKeyedService<AgentToolCollection>(agentName);
                DateTools dateTools = sp.GetRequiredService<DateTools>();
                AgentRunTracker runTracker = sp.GetRequiredService<AgentRunTracker>();
                AITool[] agentTools = roleOnly
                    ? tools.Tools
                    : [.. tools.Tools, AIFunctionFactory.Create(dateTools.GetCurrentDateTimeIso)];
                ProviderSwitchingChatClient chatClient = new(
                    agentName,
                    sp.GetRequiredService<IOptionsMonitor<AgentModelOptions>>(),
                    provider => CreateChatClient(configuration, agentName, provider));
                return new ChatClientAgent(WithDetailedToolErrors(chatClient), instructions, agentName, tools: agentTools)
                    .AsBuilder()
                    .Use((_, context, next, cancellationToken) => RecordToolCall(runTracker, context, next, cancellationToken))
                    .Build();
            });

            return toolsBuilder;
        }
    }

    private static IChatClient CreateChatClient(IConfiguration configuration, string agentName, ModelProvider provider)
    {
        string? problem = AgentProviders.Problem(configuration, provider);
        if (problem is not null)
        {
            throw new InvalidOperationException($"Agent {agentName} cannot run on {provider}: {problem}");
        }

        AgentModelOptions options = configuration.GetSection(agentName).Get<AgentModelOptions>()!;
        return provider switch
        {
            ModelProvider.Anthropic => CreateAnthropicChatClient(configuration, options),
            ModelProvider.OpenAICompatible => CreateOpenAICompatibleChatClient(configuration, options),
            _ => throw new InvalidOperationException($"Agent {agentName} has an unknown model provider '{provider}'.")
        };
    }

    private static IChatClient CreateAnthropicChatClient(IConfiguration configuration, AgentModelOptions options)
    {
        AnthropicOptions anthropic = AgentProviders.AnthropicSettings(configuration);
        AnthropicClient client = new()
        {
            ApiKey = anthropic.ApiKey,
        };

        return client.AsIChatClient(anthropic.Model, options.MaxOutputTokens);
    }

    private static IChatClient CreateOpenAICompatibleChatClient(IConfiguration configuration, AgentModelOptions options)
    {
        OpenAICompatibleOptions server = AgentProviders.OpenAICompatibleSettings(configuration);

        // The client refuses to send a request without a key, but a server started without one accepts any.
        ApiKeyCredential credential = new(string.IsNullOrWhiteSpace(server.ApiKey) ? "unused" : server.ApiKey);
        OpenAIClient client = new(credential, new OpenAIClientOptions
        {
            Endpoint = server.Endpoint,
            NetworkTimeout = server.NetworkTimeout,
        });
        IChatClient chatClient = client.GetChatClient(server.Model).AsIChatClient();

        return options.EnableThinking ? chatClient : WithoutThinking(chatClient);
    }

    /// <summary>
    /// Asks the server's chat template to skip reasoning on every request, by sending llama.cpp's (and vLLM's)
    /// <c>chat_template_kwargs.enable_thinking = false</c>. The OpenAI API has no such field, so it is patched into
    /// the request body.
    /// </summary>
    internal static IChatClient WithoutThinking(IChatClient chatClient) => chatClient
        .AsBuilder()
        .ConfigureOptions(options =>
        {
            Func<IChatClient, object?>? previous = options.RawRepresentationFactory;
            options.RawRepresentationFactory = client =>
            {
                ChatCompletionOptions completionOptions =
                    previous?.Invoke(client) as ChatCompletionOptions ?? new ChatCompletionOptions();
                // JsonPatch is the client's supported way to send fields the OpenAI API doesn't define.
#pragma warning disable SCME0001
                completionOptions.Patch.Set("$.chat_template_kwargs.enable_thinking"u8, false);
#pragma warning restore SCME0001
                return completionOptions;
            };
        })
        .Build();

    /// <summary>
    /// Invokes tools with their exception messages passed back to the model. By default the model is only told that a
    /// function failed, but the tools' errors (and refusals) are written for the model to correct its call or continue
    /// with what it may do.
    /// </summary>
    private static IChatClient WithDetailedToolErrors(IChatClient chatClient) => chatClient
        .AsBuilder()
        .UseFunctionInvocation(configure: invoker => invoker.IncludeDetailedErrors = true)
        .Build();

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
