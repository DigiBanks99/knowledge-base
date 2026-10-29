namespace KnowledgeBase.Agent.Agents.Models;

/// <summary>The provider an agent runs on, the deployment's default and what the agent could switch to.</summary>
public sealed record AgentProviderSetting(
    string Agent,
    ModelProvider Provider,
    ModelProvider DefaultProvider,
    IReadOnlyList<ProviderChoice> Choices);

/// <summary>A provider an agent can be put on, with the model it would run, or why it cannot be chosen.</summary>
public sealed record ProviderChoice(ModelProvider Provider, string Model, string? Unavailable);
