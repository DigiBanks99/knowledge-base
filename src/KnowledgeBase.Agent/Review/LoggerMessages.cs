namespace KnowledgeBase.Agent.Review;

public static partial class LoggerMessages
{
    [LoggerMessage(LogLevel.Debug, "Serializing review findings to file '{FilePath}'")]
    public static partial void SerializingReviewFindingsToFile(this ILogger logger, string filePath);

    [LoggerMessage(LogLevel.Debug, Message = "New review request received at '{FilePath}'.")]
    public static partial void NewReviewRequestRead(this ILogger logger, string filePath);

    [LoggerMessage(LogLevel.Error, "Failed to review the wiki for request '{FilePath}': {Error}")]
    public static partial void FailedToReviewWiki(this ILogger logger, string filePath, Error error);

    [LoggerMessage(LogLevel.Information, "Resolved {FindingCount} findings on {ResolvedAt}.")]
    public static partial void ResolvedFindings(this ILogger logger, int findingCount, DateTimeOffset resolvedAt);

    [LoggerMessage(LogLevel.Debug, "New finding opened at '{FilePath}'.")]
    public static partial void NewFindingOpened(this ILogger logger, string filePath);

    [LoggerMessage(LogLevel.Information, "{Result}")]
    public static partial void EditorFinished(this ILogger logger, string result);

    [LoggerMessage(LogLevel.Error, "Failed to edit the wiki: {Error}")]
    public static partial void FailedToEditWiki(this ILogger logger, Error error);

    [LoggerMessage(LogLevel.Information, "Scheduled review requested at '{FilePath}'.")]
    public static partial void ScheduledReviewRequested(this ILogger logger, string filePath);
}