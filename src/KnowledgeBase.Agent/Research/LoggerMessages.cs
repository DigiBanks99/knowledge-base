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

    [LoggerMessage(LogLevel.Information, "Discovered research topic '{Topic}'.")]
    public static partial void DiscoveredTopic(this ILogger logger, string topic);

    [LoggerMessage(LogLevel.Information, "Pruned research topic '{Topic}': its page was deleted or moved.")]
    public static partial void PrunedTopic(this ILogger logger, string topic);

    [LoggerMessage(LogLevel.Error, "Failed to update the research topics after {Agent} run '{RunId}': {Error}")]
    public static partial void FailedToRecordTopicChanges(this ILogger logger, string agent, string runId, Error error);

    [LoggerMessage(LogLevel.Information, "Adopting {Count} wiki topics with no research state and requesting their curation.")]
    public static partial void DiscoveringTopics(this ILogger logger, int count);

    [LoggerMessage(LogLevel.Error, "Failed to adopt some of the wiki's research topics; the next start retries them: {Error}")]
    public static partial void FailedToDiscoverTopics(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Information, "The author set research topic '{Topic}' muted: {Muted}.")]
    public static partial void TopicMuteSet(this ILogger logger, string topic, bool muted);
}
