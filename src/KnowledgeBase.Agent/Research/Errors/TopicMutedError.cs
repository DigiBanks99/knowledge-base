namespace KnowledgeBase.Agent.Research.Errors;

/// <summary>The author muted the topic, so it is not researched until they unmute it.</summary>
internal sealed class TopicMutedError(string topic)
    : Error("TopicMuted", $"Research topic '{topic}' is muted; unmute it to research it.")
{
    public string Topic { get; } = topic;
}
