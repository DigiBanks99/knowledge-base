using KnowledgeBase.Agent.Facts.Models;

namespace KnowledgeBase.Agent.Runs.Models;

public enum AgentRunStatus
{
    Running,
    Succeeded,
    Failed
}

public enum AgentRunTriggerKind
{
    Note,
    Facts,
    Curation,
    Triage,
    ReviewRequest,
    Findings
}

/// <summary>What started a run, and the file (or other reference) it was started for.</summary>
public sealed record AgentRunTrigger(AgentRunTriggerKind Kind, string Reference);

/// <summary>A tool the agent called during a run, and the wiki path or file it acted on.</summary>
public sealed record AgentToolCall(string Tool, string? Target, DateTimeOffset At, bool Succeeded, string? Error);

/// <summary>
/// A recorded agent run. <see cref="Origin"/> is where the content the run works on came from, and <see cref="Topic"/>
/// the wiki topic research was gathered for. Runs over research are restricted in what their tools may change, and runs
/// stored before origins were recorded read as <see cref="FactOrigin.Author"/>.
/// </summary>
public sealed record AgentRun(
    string Id,
    string Agent,
    AgentRunTrigger Trigger,
    AgentRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Summary,
    string? Error,
    IReadOnlyList<AgentToolCall> ToolCalls,
    FactOrigin Origin = FactOrigin.Author,
    string? Topic = null);
