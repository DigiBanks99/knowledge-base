using KnowledgeBase.Agent.Research.Models;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Research;

/// <summary>
/// How the research loop stands, as a status signal: what is due, in flight, backed off, muted, failing or waiting for
/// the author's review. Only failing topics and open questions make it degraded: a quiet topic backing off is the loop
/// working, but a note that keeps failing is a broken pipeline that would otherwise look like staleness, and a
/// question stops its topic being researched until the author answers it.
/// </summary>
internal sealed class ResearchLoopHealthCheck(
    IResearchStore store,
    ResearchCycleClock cycleClock,
    TimeProvider timeProvider) : IHealthCheck
{
    public const string Name = "Research loop";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<ResearchTopic>, Error> topics = await store.ListTopicsAsync(cancellationToken);
        if (topics.IsFailure)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, topics.Error.Message);
        }

        Result<IReadOnlySet<string>, Error> inFlight = await store.ListTopicsInFlightAsync(cancellationToken);
        if (inFlight.IsFailure)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, inFlight.Error.Message);
        }

        Result<IReadOnlyList<AwaitingBatch>, Error> awaiting = await store.ListAwaitingBatchesAsync(cancellationToken);
        if (awaiting.IsFailure)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, awaiting.Error.Message);
        }

        ResearchLoopCounts counts = ResearchLoopCounts.Of(
            topics.Value,
            inFlight.Value,
            awaiting.Value.Count,
            timeProvider.GetUtcNow());
        string detail = counts.Describe(cycleClock.NextCycleAt);
        return counts.Failing > 0 || counts.NeedsReview.Count > 0 ? new HealthCheckResult(context.Registration.FailureStatus, detail) : HealthCheckResult.Healthy(detail);
    }
}

/// <summary>The research loop's topics counted by what they are waiting for.</summary>
/// <param name="NeedsReview">The unmuted topics whose question waits for the author, with the question.</param>
internal sealed record ResearchLoopCounts(
    int Due,
    int Awaiting,
    int BackedOff,
    int Muted,
    int Failing,
    IReadOnlyList<TopicQuestion> NeedsReview)
{
    public static ResearchLoopCounts Of(
        IReadOnlyList<ResearchTopic> topics,
        IReadOnlySet<string> inFlight,
        int awaiting,
        DateTimeOffset now) => new(
            DueTopics.Select(topics, inFlight, now, int.MaxValue).Count,
            awaiting,
            topics.Count(topic => !topic.Muted && topic.Interval > ResearchTopic.DefaultInterval),
            topics.Count(topic => topic.Muted),
            topics.Count(topic => topic.IsFailing),
            [
                .. topics
                    .Where(topic => !topic.Muted && topic.NeedsReview)
                    .OrderBy(topic => topic.Path, StringComparer.Ordinal)
                    .Select(topic => new TopicQuestion(topic.Path, topic.ReviewQuestion!))
            ]);

    public string Describe(DateTimeOffset? nextCycleAt) =>
        $"{Due} due, {Awaiting} awaiting, {BackedOff} backed off, {Muted} muted, {Failing} failing, {NeedsReview.Count} need review; "
        + (nextCycleAt is { } next ? $"next cycle {next:yyyy-MM-dd HH:mm} UTC." : "no cycle scheduled yet.")
        + string.Concat(NeedsReview.Select(review => $" {review.Topic} asks: {Shortened(review.Question)}"));

    /// <summary>
    /// The detail is logged whenever it changes, so each question is cut short: enough to recognise it, with the whole
    /// question on the Topics page.
    /// </summary>
    private static string Shortened(string question) =>
        question.Length <= QuestionLength ? question : $"{question[..QuestionLength].TrimEnd()}…";

    private const int QuestionLength = 120;
}

/// <summary>A question the Pleb asked the author about a topic.</summary>
internal sealed record TopicQuestion(string Topic, string Question);
