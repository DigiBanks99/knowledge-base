namespace KnowledgeBase.Agent.Conversations;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Error, "The Librarian failed to answer in conversation '{ConversationId}'.")]
    public static partial void LibrarianFailed(this ILogger logger, string conversationId, Exception exception);
}