using KnowledgeBase.Agent.Research.Fetch;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>Fetches the pages of every pending batch that have not been fetched yet.</summary>
public record struct FetchPendingResultsCommand;

/// <summary>What one fetch step did.</summary>
/// <param name="Failed">Topics whose batch could not be read or written; their pages are fetched next cycle.</param>
public sealed record FetchCycle(IReadOnlyList<TopicFetchOutcome> Fetched, IReadOnlyList<string> Failed);

internal sealed class FetchPendingResultsCommandHandler(
    ILogger<FetchPendingResultsCommandHandler> logger,
    IResearchStore store,
    PendingResultFetch pendingResultFetch)
{
    /// <summary>
    /// Works through the topics with a pending batch one at a time. A page that cannot be fetched is recorded on its
    /// result; a topic whose batch cannot be read or written is logged and left for the next cycle.
    /// </summary>
    public async Task<Result<FetchCycle, Error>> Handle(FetchPendingResultsCommand _, CancellationToken cancellationToken) =>
        await store
            .ListTopicsInFlightAsync(cancellationToken)
            .Map(topics => FetchAsync([.. topics.Order(StringComparer.Ordinal)], cancellationToken));

    private async Task<FetchCycle> FetchAsync(IReadOnlyList<string> topics, CancellationToken cancellationToken)
    {
        List<TopicFetchOutcome> fetched = [];
        List<string> failed = [];
        foreach (string topic in topics)
        {
            Result<TopicFetchOutcome, Error> outcome = await pendingResultFetch.FetchAsync(topic, cancellationToken);
            if (outcome.IsSuccess)
            {
                fetched.Add(outcome.Value);
            }
            else
            {
                logger.FetchFailed(topic, outcome.Error);
                failed.Add(topic);
            }
        }

        return new FetchCycle(fetched, failed);
    }
}
