namespace KnowledgeBase.Agent.Wiki.Versioning;

public sealed class WikiRepositoryMissingError(string directory)
    : Error("WikiRepositoryMissing", $"The wiki directory '{directory}' is not a git repository.");
