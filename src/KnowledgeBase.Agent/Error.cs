namespace KnowledgeBase.Agent;

public abstract class Error(
    string code,
    string message) : ICombine
{
    public string Code { get; } = code;
    public string Message { get; } = message;

    public ICombine Combine(ICombine value) => value switch
    {
        AggregateError aggregateError => new AggregateError([.. aggregateError.Errors, this]),
        Error error => new AggregateError([this, error]),
        _ => this
    };

    public override string ToString() => $"{Code}: {Message}";
}

public class ValidationError(string code, string message) : Error(code, message);

public class AggregateError(IEnumerable<Error> errors) : Error("AggregateError", "Multiple errors occurred")
{
    public IEnumerable<Error> Errors { get; } = errors;
}
