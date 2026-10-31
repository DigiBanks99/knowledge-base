namespace KnowledgeBase.Agent.Research.Errors;

/// <summary>
/// The author edited the topic's brief and queries, so the Pleb may not replace them until the author asks for a new
/// curation.
/// </summary>
internal sealed class TopicPinnedError(string topic)
    : Error("TopicPinned", $"Research topic '{topic}' has a brief and queries the author set by hand, so they cannot be changed now. Stop without calling another tool.")
{
    public string Topic { get; } = topic;
}
