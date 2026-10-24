namespace KnowledgeBase.Agent.Anthropic;

public sealed class AnthropicOptions
{
    public required string ApiKey { get; init; }
    public string Model { get; init; } = "claude-haiku-5-5";
    public required string Instructions { get; init; }
}
