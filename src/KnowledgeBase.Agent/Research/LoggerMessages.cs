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

    [LoggerMessage(LogLevel.Information, "The Pleb set {Count} queries for research topic '{Topic}'.")]
    public static partial void TopicQueriesSet(this ILogger logger, string topic, int count);

    [LoggerMessage(LogLevel.Information, "The Pleb kept the queries of research topic '{Topic}'.")]
    public static partial void TopicQueriesKept(this ILogger logger, string topic);

    [LoggerMessage(LogLevel.Information, "The Pleb asked the author to review research topic '{Topic}'.")]
    public static partial void TopicReviewRequested(this ILogger logger, string topic);

    [LoggerMessage(LogLevel.Information, "The author reviewed research topic '{Topic}': {Review}.")]
    public static partial void TopicReviewed(this ILogger logger, string topic, string review);

    [LoggerMessage(LogLevel.Information, "Skipping the curation cycle: the model server is not healthy.")]
    public static partial void CurationSkippedModelServerUnhealthy(this ILogger logger);

    [LoggerMessage(LogLevel.Information, "Curating {Count} of {Waiting} waiting research topics.")]
    public static partial void CuratingTopics(this ILogger logger, int count, int waiting);

    [LoggerMessage(LogLevel.Warning, "Curation of research topic '{Topic}' failed: {Reason}")]
    public static partial void CurationFailed(this ILogger logger, string topic, string reason);

    [LoggerMessage(LogLevel.Warning, "Curation of research topic '{Topic}' was left waiting: {Error}")]
    public static partial void CurationLeftWaiting(this ILogger logger, string topic, Error error);

    [LoggerMessage(LogLevel.Error, "The curation cycle failed: {Error}")]
    public static partial void CurationCycleFailed(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Information, "Searching {Count} due research topics.")]
    public static partial void SearchingTopics(this ILogger logger, int count);

    [LoggerMessage(LogLevel.Information, "Searched research topic '{Topic}': {Found} results found, {Kept} kept for triage.")]
    public static partial void TopicSearched(this ILogger logger, string topic, int found, int kept);

    [LoggerMessage(LogLevel.Warning, "A search source failed while searching research topic '{Topic}': {Error}")]
    public static partial void SearchSourceFailed(this ILogger logger, string topic, Error error);

    [LoggerMessage(LogLevel.Warning, "The search of research topic '{Topic}' failed; it stays due: {Error}")]
    public static partial void TopicSearchFailed(this ILogger logger, string topic, Error error);

    [LoggerMessage(LogLevel.Error, "The search cycle failed: {Error}")]
    public static partial void SearchCycleFailed(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Error, "The search cycle failed unexpectedly; the next cycle runs as usual.")]
    public static partial void SearchCycleCrashed(this ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Information, "Fetched the pending results of research topic '{Topic}': {Fetched} fetched, {Failed} could not be.")]
    public static partial void TopicFetched(this ILogger logger, string topic, int fetched, int failed);

    [LoggerMessage(LogLevel.Information, "A result of research topic '{Topic}' could not be fetched from {Url}: {Error}")]
    public static partial void PageNotFetched(this ILogger logger, string topic, string url, Error error);

    [LoggerMessage(LogLevel.Warning, "A result of research topic '{Topic}' could not be fetched from {Url}: the fetcher failed unexpectedly.")]
    public static partial void PageFetchCrashed(this ILogger logger, string topic, string url, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Fetching the pending results of research topic '{Topic}' failed; the next cycle retries: {Error}")]
    public static partial void FetchFailed(this ILogger logger, string topic, Error error);

    [LoggerMessage(LogLevel.Error, "Fetching the pending results of research topic '{Topic}' failed unexpectedly; the next cycle retries.")]
    public static partial void FetchCrashed(this ILogger logger, string topic, Exception exception);

    [LoggerMessage(LogLevel.Error, "The fetch step of the search cycle failed: {Error}")]
    public static partial void FetchCycleFailed(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Information, "Skipping the triage cycle: the model server is not healthy.")]
    public static partial void TriageSkippedModelServerUnhealthy(this ILogger logger);

    [LoggerMessage(LogLevel.Warning, "A result of research topic '{Topic}' ({Url}) was left unjudged: {Error}")]
    public static partial void ResultLeftWaiting(this ILogger logger, string topic, string url, Error error);

    [LoggerMessage(LogLevel.Warning, "A result of research topic '{Topic}' ({Url}) got no verdict and counts as discarded: {Reason}")]
    public static partial void ResultNotJudged(this ILogger logger, string topic, string url, string reason);

    [LoggerMessage(LogLevel.Information, "Triaged research topic '{Topic}': {Kept} of {Count} results kept in note '{Note}'.")]
    public static partial void TopicTriaged(this ILogger logger, string topic, int kept, int count, string note);

    [LoggerMessage(LogLevel.Information, "Triaged research topic '{Topic}': all {Count} results were discarded.")]
    public static partial void TopicTriagedEmpty(this ILogger logger, string topic, int count);

    [LoggerMessage(LogLevel.Error, "The triage cycle failed: {Error}")]
    public static partial void TriageCycleFailed(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Information, "Research on topic '{Topic}' resolved as {Outcome}; the topic is failing: {Failing}.")]
    public static partial void BatchResolved(this ILogger logger, string topic, string outcome, bool failing);

    [LoggerMessage(LogLevel.Information, "Pruned {Count} completed research batches past their retention.")]
    public static partial void CompletedBatchesPruned(this ILogger logger, int count);

    [LoggerMessage(LogLevel.Error, "Resolving the awaiting research batches failed: {Error}")]
    public static partial void ResolveCycleFailed(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Warning, "Failed to record the search of research topic '{Topic}'; the search carries on.")]
    public static partial void FailedToRecordSearch(this ILogger logger, string topic, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Failed to add what became of its results to search run '{RunId}'.")]
    public static partial void FailedToAppendToSearchRun(this ILogger logger, string runId, Exception exception);
}
