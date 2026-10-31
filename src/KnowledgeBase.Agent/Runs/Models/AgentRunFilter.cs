namespace KnowledgeBase.Agent.Runs.Models;

/// <summary>Which runs to list; a null criterion matches every run.</summary>
/// <param name="Agent">The agent's name, matched ignoring case.</param>
/// <param name="TriggerReference">The file (or other reference) the run was started for, matched ignoring case.</param>
/// <param name="Topic">The research topic's wiki path, matched exactly as wiki paths are case-sensitive.</param>
public sealed record AgentRunFilter(
    string? Agent = null,
    AgentRunTriggerKind? TriggerKind = null,
    string? TriggerReference = null,
    string? Topic = null)
{
    public static AgentRunFilter All { get; } = new();

    public bool Matches(AgentRun run) =>
        (Agent is null || run.Agent.Equals(Agent, StringComparison.OrdinalIgnoreCase))
        && (TriggerKind is null || run.Trigger.Kind == TriggerKind)
        && (TriggerReference is null || run.Trigger.Reference.Equals(TriggerReference, StringComparison.OrdinalIgnoreCase))
        && (Topic is null || string.Equals(run.Topic, Topic, StringComparison.Ordinal));
}
