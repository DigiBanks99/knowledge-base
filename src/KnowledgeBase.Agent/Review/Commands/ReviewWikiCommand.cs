using KnowledgeBase.Agent.Review.Models;
using KnowledgeBase.Agent.Review.Queries;

using KnowledgeBase.Agent.Wiki;

using Microsoft.Agents.AI;

namespace KnowledgeBase.Agent.Review.Commands;

public record ReviewWikiCommand(DateTimeOffset RequestTime);

internal sealed class ReviewWikiCommandHandler(
    [FromKeyedServices(KnownAgents.Reviewer)] AIAgent reviewer,
    WikiLock wikiLock,
    ListFindingsQueryHandler listFindingsQueryHandler)
{
    public async Task<Result<string, Error>> Handle(
        ReviewWikiCommand request,
        CancellationToken cancellationToken)
    {
        const string prompt =
            """
            Perform a full review of the wiki against the wiki rules.
            Check the open findings first so you do not record duplicates, then record your findings.
            """;
        using (await wikiLock.AcquireAsync(cancellationToken))
        {
            await reviewer.RunAsync(prompt, cancellationToken: cancellationToken);
        }

        return await listFindingsQueryHandler
            .Handle(new ListFindingsQuery(FindingState.Open), cancellationToken)
            .Map(findings => $"Review requested at {request.RequestTime:O} completed. {findings.Count} findings are open.");
    }
}
