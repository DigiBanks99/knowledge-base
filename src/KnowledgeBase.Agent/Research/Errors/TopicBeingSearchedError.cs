namespace KnowledgeBase.Agent.Research.Errors;

/// <summary>The topic is being searched right now, by the search cycle or an earlier request.</summary>
internal sealed class TopicBeingSearchedError(string topic)
    : Error("TopicBeingSearched", $"Research topic '{topic}' is being searched right now; its results appear once the search finishes.")
{
    public string Topic { get; } = topic;
}
