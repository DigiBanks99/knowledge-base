namespace KnowledgeBase.Agent.Research.Errors;

/// <summary>The Pleb asked the author a question about the topic, so it is not searched or approved until it is answered.</summary>
internal sealed class TopicNeedsReviewError(string topic)
    : Error("TopicNeedsReview", $"Research topic '{topic}' needs review: answer the Pleb's question to have it curated again.")
{
    public string Topic { get; } = topic;
}
