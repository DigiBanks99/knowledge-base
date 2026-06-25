namespace KnowledgeBase.Agent;

public abstract class Error(
    string code,
    string message)
{
    public string Code { get; } = code;
    public string Message { get; } = message;

    public override string ToString() => $"{Code}: {Message}";
}

public class ValidationError(string code, string message) : Error(code, message);