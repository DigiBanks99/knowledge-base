using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Error, "Failed to add the people and organisations of a fact document to the deny-list: {Error}")]
    public static partial void FailedToRecordFactSubjects(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Information, "Seeding the deny-list from {Count} people folders.")]
    public static partial void SeedingDenyList(this ILogger logger, int count);

    [LoggerMessage(LogLevel.Error, "Failed to seed the deny-list from the people folders; the next start retries it: {Error}")]
    public static partial void FailedToSeedDenyList(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Information, "The author set deny-list entry '{Name}' to {State}.")]
    public static partial void DenyListEntrySet(this ILogger logger, string name, DenyListState state);
}
