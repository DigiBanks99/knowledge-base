using System.Text.Json.Serialization;

namespace KnowledgeBase.Agent.Facts.Models;

/// <summary>
/// What kind of thing a fact subject is; people and organisations are private by default. A
/// <see cref="PublicFigure"/> is a person who matters to the wiki only as the author or expert behind a source, so their
/// name may be researched and they get no page of their own.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubjectKind>))]
public enum SubjectKind
{
    Person,
    PublicFigure,
    Organisation,
    Other
}
