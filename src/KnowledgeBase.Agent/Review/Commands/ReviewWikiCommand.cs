using KnowledgeBase.Agent.Review.Models;
using KnowledgeBase.Agent.Review.Queries;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Agents.AI;

namespace KnowledgeBase.Agent.Review.Commands;

public record ReviewWikiCommand(string RequestName, DateTimeOffset RequestTime);

internal sealed class ReviewWikiCommandHandler(
    [FromKeyedServices(KnownAgents.Reviewer)] AIAgent reviewer,
    WikiLock wikiLock,
    AgentRunTracker runTracker,
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

        Result<AgentRun, Error> run;
        using (await wikiLock.AcquireAsync(cancellationToken))
        {
            run = await runTracker.RunAsync(
                KnownAgents.Reviewer,
                new AgentRunTrigger(AgentRunTriggerKind.ReviewRequest, request.RequestName),
                token => reviewer.RunAsync(prompt, cancellationToken: token),
                cancellationToken);
        }

        if (run.IsFailure)
        {
            return run.Error;
        }

        return await listFindingsQueryHandler
            .Handle(new ListFindingsQuery(FindingState.Open), cancellationToken)
            .Map(findings => $"Review requested at {request.RequestTime:O} completed. {findings.Count} findings are open.");
    }
}