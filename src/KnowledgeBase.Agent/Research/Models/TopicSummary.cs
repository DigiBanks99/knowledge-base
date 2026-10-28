using System.Text.Json.Serialization;

namespace KnowledgeBase.Agent.Research.Models;

/// <summary>Where a topic is in the research loop, as the folders holding its work say.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TopicState>))]
public enum TopicState
{
    Idle,
    AwaitingCuration,
    CurationFailed,
    Muted,

    /// <summary>The topic's last search kept results that wait for the Scout.</summary>
    Triaging
}

/// <summary>A topic as the Topics page shows it.</summary>
/// <param name="Slug">The topic's path in the form the API's topic routes take.</param>
/// <param name="NextSearchAt">When the topic is next due to be searched, once it has queries to search.</param>
/// <param name="PendingResults">How many results of its last search wait to be triaged.</param>
public sealed record TopicSummary(
    string Path,
    string Slug,
    TopicState State,
    string? Brief,
    IReadOnlyList<string> Queries,
    bool Muted,
    DateTimeOffset? LastCuratedAt,
    DateTimeOffset? LastSearchedAt = null,
    DateTimeOffset? NextSearchAt = null,
    int PendingResults = 0);
