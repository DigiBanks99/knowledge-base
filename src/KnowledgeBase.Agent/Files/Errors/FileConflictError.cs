namespace KnowledgeBase.Agent.Files.Errors;

public class FileConflictError(string code, string message) : Error(code, message);