namespace KnowledgeBase.Agent.Wiki.Versioning;

public sealed class GitCommandFailedError(string command, string reason)
    : Error("GitCommandFailed", $"'git {command}' failed: {reason}")
{
    public string Command => command;
}
