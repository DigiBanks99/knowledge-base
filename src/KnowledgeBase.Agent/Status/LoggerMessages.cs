namespace KnowledgeBase.Agent.Status;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Information, "Status signal '{Signal}' is {State}: {Detail}")]
    public static partial void StatusSignalChanged(this ILogger logger, string signal, StatusSignalState state, string? detail);
}