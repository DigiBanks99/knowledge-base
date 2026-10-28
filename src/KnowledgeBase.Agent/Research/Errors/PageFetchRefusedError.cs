namespace KnowledgeBase.Agent.Research.Errors;

/// <summary>
/// The fetcher would not request a page: its address is not public, its scheme or port is not allowed, or robots.txt
/// disallows it.
/// </summary>
internal sealed class PageFetchRefusedError(string url, string reason)
    : Error("PageFetchRefused", $"Refused to fetch {url}: {reason}")
{
    public string Reason { get; } = reason;
}
