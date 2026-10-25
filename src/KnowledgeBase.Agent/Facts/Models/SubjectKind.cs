using System.Text.Json.Serialization;

namespace KnowledgeBase.Agent.Facts.Models;

/// <summary>What kind of thing a fact subject is; people and organisations are private by default.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubjectKind>))]
public enum SubjectKind
{
    Person,
    Organisation,
    Other
}
