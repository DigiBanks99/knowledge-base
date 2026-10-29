namespace KnowledgeBase.Agent.Agents.Errors;

internal sealed class UnknownAgentError(string agent)
    : Error("UnknownAgent", $"There is no agent named '{agent}'.");
