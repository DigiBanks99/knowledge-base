using KnowledgeBase.Agent.Review.Models;
using KnowledgeBase.Agent.Review.Queries;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Agents.AI;

namespace KnowledgeBase.Agent.Review.Commands;

public record EditWikiCommand;

internal sealed class EditWikiCommandHandler(
    [FromKeyedServices(KnownAgents.Editor)] AIAgent editor,
    WikiLock wikiLock,
    AgentRunTracker runTracker,
    ListFindingsQueryHandler listFindingsQueryHandler)
{
    public async Task<Result<string, Error>> Handle(
        EditWikiCommand _,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyCollection<ReviewFinding>, Error> openFindings = await listFindingsQueryHandler
            .Handle(new ListFindingsQuery(FindingState.Open), cancellationToken);
        if (openFindings.IsFailure)
        {
            return openFindings.Error;
        }

        int openCount = openFindings.Value.Count;
        if (openCount == 0)
        {
            return "There are no open findings to resolve.";
        }

        const string prompt = "Resolve the open review findings by editing the wiki, then mark each one as resolved.";
        Result<AgentRun, Error> run;
        using (await wikiLock.AcquireAsync(cancellationToken))
        {
            run = await runTracker.RunAsync(
                KnownAgents.Editor,
                new AgentRunTrigger(AgentRunTriggerKind.Findings, string.Join(", ", openFindings.Value.Select(finding => finding.Id))),
                token => editor.RunAsync(prompt, cancellationToken: token),
                cancellationToken);
        }

        if (run.IsFailure)
        {
            return run.Error;
        }

        return await listFindingsQueryHandler
            .Handle(new ListFindingsQuery(FindingState.Open), cancellationToken)
            .Map(remaining => $"Editor resolved {openCount - remaining.Count} of {openCount} open findings.");
    }
}