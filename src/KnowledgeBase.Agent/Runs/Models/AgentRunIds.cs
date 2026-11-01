namespace KnowledgeBase.Agent.Runs.Models;

/// <summary>
/// The shape of a run's id: its start time, so ids sort with runs, then the name of what ran and a random suffix. The
/// run store accepts only ids of letters, digits and dashes.
/// </summary>
internal static class AgentRunIds
{
    public static string New(string name, DateTimeOffset startedAt) =>
        $"{startedAt:yyyyMMdd-HHmmss}-{name.ToLowerInvariant()}-{Guid.NewGuid():N}"[..40];
}
