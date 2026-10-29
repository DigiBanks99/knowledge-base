using System.Text.Json.Serialization;

using KnowledgeBase.Agent.Facts.Models;

namespace KnowledgeBase.Agent.Research.Models;

/// <summary>Who decided an entry: classification of the author's own notes, or the author from the UI.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DenyListSource>))]
public enum DenyListSource
{
    Auto,
    Manual
}

/// <summary>
/// Whether a name is kept out of search queries (<see cref="Denied"/>) or may appear in them. A
/// <see cref="Pending"/> name might be a public figure and waits for the author; until then it is kept out like a
/// denied one.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DenyListState>))]
public enum DenyListState
{
    Denied,
    Allowed,
    Pending
}

/// <summary>A person or organisation whose name must not leave the machine in a search query, unless allowed.</summary>
public sealed record DenyListEntry(string Name, SubjectKind Kind, DenyListSource Source, DenyListState State);
