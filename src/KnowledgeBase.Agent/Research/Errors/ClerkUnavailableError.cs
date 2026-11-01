namespace KnowledgeBase.Agent.Research.Errors;

/// <summary>A Clerk run the model server could not serve: it timed out or could not be reached.</summary>
internal sealed class ClerkUnavailableError(string reason) : Error("ClerkUnavailable", reason);
