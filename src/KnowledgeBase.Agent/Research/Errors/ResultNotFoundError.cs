namespace KnowledgeBase.Agent.Research.Errors;

internal sealed class ResultNotFoundError(string topic, string url)
    : Error("ResultNotFound", $"Research topic '{topic}' has no result '{url}' from that search; its batch may have been pruned.");
