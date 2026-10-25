namespace KnowledgeBase.Agent.Facts.Models;

/// <summary>
/// A persisted set of facts, together with the note they were extracted from and where that note came from. Documents
/// written before provenance was recorded read as <see cref="FactOrigin.Author"/>.
/// </summary>
public record FactDocument(
    string? SourceNote,
    Fact[] Facts,
    FactOrigin Origin = FactOrigin.Author,
    string? Topic = null);