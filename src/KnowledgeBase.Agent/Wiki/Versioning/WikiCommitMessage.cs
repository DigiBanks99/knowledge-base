using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Wiki.Versioning;

/// <summary>
/// How a wiki commit is attributed. Each agent is the author of its runs' commits, so <c>git log --author</c> filters
/// the history by agent.
/// </summary>
internal static class WikiCommitMessage
{
    public const string CommitterName = "Knowledge Base";
    public const string Email = "knowledge-base@localhost";
    public const string InitialCommit = "Initial wiki";
    public const int MaxSummaryLength = 72;

    public static string DefaultAuthor => Author(CommitterName);

    public static string Author(string name) => $"{name} <{Email}>";

    /// <summary>
    /// The subject names the agent and what it did (or why it failed); the body identifies the run, so the commit can
    /// be traced back to it on the Runs page, and where its content came from, so <c>git log --grep "Origin: Research"</c>
    /// finds every change research made.
    /// </summary>
    public static string For(AgentRun run)
    {
        bool failed = run.Status == AgentRunStatus.Failed;
        string summary = SummaryLine(failed ? run.Error : run.Summary) ?? (failed ? "Run failed" : "Run finished");
        string topic = run.Topic is null ? string.Empty : $"\nTopic: {run.Topic}";
        return $"""
            {run.Agent}: {summary}

            Run: {run.Id}
            Status: {run.Status}
            Trigger: {run.Trigger.Kind} {run.Trigger.Reference}
            Origin: {run.Origin}{topic}
            """;
    }

    private static string? SummaryLine(string? text)
    {
        string? line = text?
            .Split('\n')
            .Select(candidate => candidate.Trim().TrimStart('#').Trim())
            .FirstOrDefault(candidate => candidate.Length > 0);
        return line is null || line.Length <= MaxSummaryLength
            ? line
            : $"{line[..(MaxSummaryLength - 3)].TrimEnd()}...";
    }
}