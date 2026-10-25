namespace KnowledgeBase.Agent.Notes.Models;

public record Notes(
    IEnumerable<string> New,
    IEnumerable<string> Processing,
    IEnumerable<string> Processed,
    IEnumerable<string> Failed);
