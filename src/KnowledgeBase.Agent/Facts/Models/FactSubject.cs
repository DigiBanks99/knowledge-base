using System.ComponentModel;

namespace KnowledgeBase.Agent.Facts.Models;

/// <summary>
/// A subject facts are about. The kind is nullable so a subject the model left unclassified can be refused rather
/// than silently read as one of the kinds; it is only missing in documents written before subjects were classified.
/// </summary>
public record FactSubject(
    string Name,
    string? Description,
    [property: Description("Required. Whether the subject is a `Person`, an `Organisation` (company, team, community, institution) or `Other` (project, topic, technology, place, the author's own work).")]
    SubjectKind? Kind);