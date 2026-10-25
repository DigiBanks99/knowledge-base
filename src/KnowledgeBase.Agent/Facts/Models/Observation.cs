using System.ComponentModel;

namespace KnowledgeBase.Agent.Facts.Models;

/// <summary>An observation about a subject. Only observations from research notes carry their sources.</summary>
public record Observation(
    string Summary,
    string Detail,
    int Priority,
    FactSubject[] RelatedSubjects,
    [property: Description("For a research note: the URLs from the note's header that this observation came from. Leave empty for the author's own notes.")]
    string[]? Sources = null);