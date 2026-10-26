namespace KnowledgeBase.Agent.Research.Errors;

internal sealed class TopicNotFoundError(string topic)
    : Error("TopicNotFound", $"There is no research topic '{topic}'.")
{
    public string Topic { get; } = topic;
}
