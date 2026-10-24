namespace KnowledgeBase.Agent.Facts.Models;

public record Fact(
    FactSubject Subject,
    Observation[] Observations);