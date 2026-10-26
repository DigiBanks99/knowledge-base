using System.Text.Json.Serialization;

namespace KnowledgeBase.Agent.Research.Models;

/// <summary>Where a topic is in the research loop, as the folders holding its work say.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TopicState>))]
public enum TopicState
{
    Idle,
    AwaitingCuration,
    Muted
}

/// <summary>A topic as the Topics page shows it.</summary>
/// <param name="Slug">The topic's path in the form the API's topic routes take.</param>
public sealed record TopicSummary(
    string Path,
    string Slug,
    TopicState State,
    string? Brief,
    IReadOnlyList<string> Queries,
    bool Muted);
