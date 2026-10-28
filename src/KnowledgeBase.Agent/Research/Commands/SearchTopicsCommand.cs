using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>Searches the topics that are due: one search cycle.</summary>
public record struct SearchTopicsCommand;

/// <summary>What one search cycle did.</summary>
/// <param name="Failed">Topics whose search failed; they stay due for the next cycle.</param>
public sealed record SearchCycle(IReadOnlyList<TopicSearchOutcome> Searched, IReadOnlyList<string> Failed);

internal sealed class SearchTopicsCommandHandler(
    ILogger<SearchTopicsCommandHandler> logger,
    IOptions<ResearchSettings> settings,
    IResearchStore store,
    TopicSearch topicSearch,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Searches up to <see cref="ResearchSettings.TopicsPerCycle"/> due topics, most overdue first. A topic whose search
    /// fails is logged and left due; the rest of the cycle carries on.
    /// </summary>
    public async Task<Result<SearchCycle, Error>> Handle(SearchTopicsCommand _, CancellationToken cancellationToken) =>
        await store
            .ListTopicsAsync(cancellationToken)
            .Bind(async topics => await store
                .ListTopicsInFlightAsync(cancellationToken)
                .Map(inFlight => DueTopics.Select(topics, inFlight, timeProvider.GetUtcNow(), settings.Value.TopicsPerCycle)))
            .Map(due => SearchAsync(due, cancellationToken));

    private async Task<SearchCycle> SearchAsync(IReadOnlyList<ResearchTopic> due, CancellationToken cancellationToken)
    {
        if (due.Count > 0)
        {
            logger.SearchingTopics(due.Count);
        }

        List<TopicSearchOutcome> searched = [];
        List<string> failed = [];
        foreach (ResearchTopic topic in due)
        {
            Result<TopicSearchOutcome, Error> outcome = await topicSearch.SearchAsync(topic.Path, cancellationToken);
            if (outcome.IsSuccess)
            {
                searched.Add(outcome.Value);
            }
            else
            {
                logger.TopicSearchFailed(topic.Path, outcome.Error);
                failed.Add(topic.Path);
            }
        }

        return new SearchCycle(searched, failed);
    }
}
