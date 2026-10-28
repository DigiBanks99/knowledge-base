namespace KnowledgeBase.Agent.Research.Errors;

/// <summary>The Pleb has not set the topic's queries yet, so there is nothing to search.</summary>
internal sealed class TopicNotCuratedError(string topic)
    : Error("TopicNotCurated", $"Research topic '{topic}' has no queries yet; it is searched once the Pleb has curated it.")
{
    public string Topic { get; } = topic;
}
