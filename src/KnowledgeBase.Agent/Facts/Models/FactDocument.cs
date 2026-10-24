namespace KnowledgeBase.Agent.Facts.Models;

/// <summary>A persisted set of facts, together with the note they were extracted from.</summary>
public record FactDocument(string? SourceNote, Fact[] Facts);
