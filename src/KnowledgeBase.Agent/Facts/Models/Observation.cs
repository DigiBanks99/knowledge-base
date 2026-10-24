namespace KnowledgeBase.Agent.Facts.Models;

public record Observation(string Summary, string Detail, int Priority, FactSubject[] RelatedSubjects);