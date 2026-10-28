namespace KnowledgeBase.Agent.Research.Errors;

/// <summary>
/// A page was requested but gave nothing to triage: the request failed or timed out, or the answer was an error, too
/// large, of a type that cannot be read or had no readable content.
/// </summary>
internal sealed class PageFetchFailedError(string url, string reason)
    : Error("PageFetchFailed", $"Could not fetch {url}: {reason}")
{
    public string Reason { get; } = reason;
}
