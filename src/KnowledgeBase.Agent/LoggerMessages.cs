namespace KnowledgeBase.Agent;

internal static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Error, "An error occurred: {Error}")]
    public static partial void ErrorOccurred(this ILogger logger, Error error);
}