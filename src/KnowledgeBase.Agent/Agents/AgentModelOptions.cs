namespace KnowledgeBase.Agent.Agents;

public enum ModelProvider
{
    Anthropic,
    OpenAICompatible
}

/// <summary>
/// The model settings of one agent, bound from the configuration section named after the agent.
/// </summary>
public sealed class AgentModelOptions
{
    public ModelProvider Provider { get; init; } = ModelProvider.Anthropic;

    /// <summary>The Anthropic API key. Only the Anthropic provider uses it.</summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// The model name. A llama.cpp server running a single model answers with that model whatever name is sent.
    /// </summary>
    public string Model { get; init; } = "claude-haiku-5-5";

    public required string Instructions { get; init; }
}

/// <summary>
/// A server with an OpenAI-compatible chat completions API, such as llama.cpp's llama-server, shared by every agent
/// whose provider is <see cref="ModelProvider.OpenAICompatible"/>.
/// </summary>
public sealed class OpenAICompatibleOptions
{
    public const string SectionName = "OpenAICompatible";

    /// <summary>The base address of the API, including the version segment, e.g. <c>http://localhost:8080/v1</c>.</summary>
    public Uri? Endpoint { get; init; }

    /// <summary>The key the server was started with (llama-server's <c>--api-key</c>), if any.</summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// How long a single completion may take. Local models on a CPU can spend minutes reading a long prompt, so this
    /// is far longer than the client's default.
    /// </summary>
    public TimeSpan NetworkTimeout { get; init; } = TimeSpan.FromMinutes(10);
}