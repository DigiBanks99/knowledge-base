namespace KnowledgeBase.Agent.Research.Models;

/// <summary>A result kept by a topic's search, with the query that found it.</summary>
/// <param name="Url">The result's normalised URL, under which the topic records it as seen.</param>
public sealed record BatchResult(string Query, string Url, SearchResult Result);

/// <summary>
/// The results one search of a topic kept, waiting for the Scout. While it waits the topic is in flight and is not
/// searched again.
/// </summary>
public sealed record ResearchBatch(string Topic, DateTimeOffset SearchedAt, IReadOnlyList<BatchResult> Results);
