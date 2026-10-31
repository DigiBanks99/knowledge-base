using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research.Queries;

/// <summary>One page of the runs recorded for a research topic, newest first.</summary>
/// <param name="Slug">The topic's slug, as the Topics page lists it.</param>
/// <param name="Page">The page, counted from 1 for the newest runs.</param>
public record ListTopicRunsQuery(string Slug, int Page);

/// <summary>A page of a topic's runs, and whether there are older ones to page back to.</summary>
public sealed record TopicRunsPage(IReadOnlyList<AgentRun> Runs, int Page, bool HasOlder);

internal sealed class ListTopicRunsQueryHandler(IAgentRunStore runStore, WikiPaths wikiPaths)
{
    public const int PageSize = 10;

    /// <summary>
    /// The topic need not still exist, so the runs of a topic that has since been pruned stay readable. One run more
    /// than a page is read to tell whether an older page exists without counting them all.
    /// </summary>
    public async Task<Result<TopicRunsPage, Error>> Handle(ListTopicRunsQuery request, CancellationToken cancellationToken)
    {
        // The upper bound keeps the offset from overflowing into a negative number, which would serve the first page.
        int page = Math.Clamp(request.Page, 1, int.MaxValue / PageSize);
        return await TopicSlugs
            .Resolve(request.Slug, wikiPaths)
            .Map(async topic => await runStore.ListAsync(
                new AgentRunFilter(Topic: topic),
                (page - 1) * PageSize,
                PageSize + 1,
                cancellationToken))
            .Map(runs => new TopicRunsPage([.. runs.Take(PageSize)], page, runs.Count > PageSize));
    }
}
