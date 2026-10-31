namespace KnowledgeBase.Agent.Research.Errors;

/// <summary>The author answered a question the Pleb has not asked: the topic does not need review.</summary>
internal sealed class TopicNotInReviewError(string topic)
    : Error("TopicNotInReview", $"Research topic '{topic}' has no open question to clarify; reject or recurate it instead.")
{
    public string Topic { get; } = topic;
}
