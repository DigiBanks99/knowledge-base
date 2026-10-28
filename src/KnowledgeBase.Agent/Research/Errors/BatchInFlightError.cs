namespace KnowledgeBase.Agent.Research.Errors;

/// <summary>
/// The topic's last results are still waiting to be triaged, so searching again would replace them before they were
/// judged (their URLs are already recorded as seen, so they would never come back).
/// </summary>
internal sealed class BatchInFlightError(string topic)
    : Error("BatchInFlight", $"Research topic '{topic}' already has results waiting to be triaged; it is searched again once they are.")
{
    public string Topic { get; } = topic;
}
