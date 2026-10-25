namespace KnowledgeBase.Agent.Research.Errors;

internal sealed class DenyListEntryNotFoundError(string name)
    : Error("DenyListEntryNotFound", $"The deny-list has no entry named '{name}'.")
{
    public string Name { get; } = name;
}
