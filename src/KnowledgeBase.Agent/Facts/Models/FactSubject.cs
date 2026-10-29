using System.ComponentModel;

namespace KnowledgeBase.Agent.Facts.Models;

/// <summary>
/// A subject facts are about. The kind is nullable so a subject the model left unclassified can be refused rather
/// than silently read as one of the kinds; it is only missing in documents written before subjects were classified.
/// </summary>
/// <param name="PossiblyPublicFigure">
/// Set when the model could not tell a public figure from someone the author knows, so the author decides instead.
/// </param>
public record FactSubject(
    string Name,
    string? Description,
    [property: Description("Required. Whether the subject is a `Person` the author knows, a `PublicFigure` (someone who matters only as the author or expert behind an article, talk, book or other source), an `Organisation` (company, team, community, institution) or `Other` (project, topic, technology, place, the author's own work).")]
    SubjectKind? Kind,
    [property: Description("Optional. `true` when the subject might be a public figure but the note does not make it clear; give such a subject the `kind` `Person` and the author will decide. Leave it out otherwise.")]
    bool? PossiblyPublicFigure = null);
