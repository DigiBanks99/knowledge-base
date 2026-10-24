namespace KnowledgeBase.Agent.Wiki.Errors;

public sealed class SerializationError(string json, Type targetType)
    : Error("JsonSerializationError", $"Failed to deserialize '{json}' into {targetType.FullName}.")
{
    public string Json => json;
    public Type TargetType => targetType;
}
