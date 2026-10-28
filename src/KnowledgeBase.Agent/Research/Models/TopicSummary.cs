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
    Triaging,

    /// <summary>A note ending in failure three times running, which says the pipeline is broken rather than the topic quiet.</summary>
    Failing
}

/// <summary>A topic as the Topics page shows it.</summary>
/// <param name="Slug">The topic's path in the form the API's topic routes take.</param>
/// <param name="NextSearchAt">When the topic is next due to be searched, once it has queries to search.</param>
/// <param name="PendingResults">How many results of its last search wait to be triaged.</param>
/// <param name="Interval">How long after a search the topic is due again.</param>
/// <param name="LastPositiveAt">When the ThreadWeaver last filed something from the topic's research.</param>
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
    int PendingResults = 0,
    TimeSpan? Interval = null,
    DateTimeOffset? LastPositiveAt = null);
