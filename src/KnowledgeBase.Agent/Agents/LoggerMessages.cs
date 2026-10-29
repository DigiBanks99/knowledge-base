namespace KnowledgeBase.Agent.Agents;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Information, "Agent {Agent} now runs on {Provider}")]
    public static partial void AgentProviderSet(this ILogger logger, string agent, ModelProvider provider);
}
