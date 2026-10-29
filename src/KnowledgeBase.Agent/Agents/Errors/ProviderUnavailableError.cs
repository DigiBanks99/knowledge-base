namespace KnowledgeBase.Agent.Agents.Errors;

/// <summary>The provider is not configured, so putting the agent there would fail its next run.</summary>
internal sealed class ProviderUnavailableError(string agent, ModelProvider provider, string problem)
    : Error("ProviderUnavailable", $"The {agent} cannot run on {provider}. {problem}");
