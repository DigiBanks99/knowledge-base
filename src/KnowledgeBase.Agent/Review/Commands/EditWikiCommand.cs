using KnowledgeBase.Agent.Review.Models;
using KnowledgeBase.Agent.Review.Queries;

using KnowledgeBase.Agent.Wiki;

using Microsoft.Agents.AI;

namespace KnowledgeBase.Agent.Review.Commands;

public record EditWikiCommand;

internal sealed class EditWikiCommandHandler(
    [FromKeyedServices(KnownAgents.Editor)] AIAgent editor,
    WikiLock wikiLock,
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
        using (await wikiLock.AcquireAsync(cancellationToken))
        {
            await editor.RunAsync(prompt, cancellationToken: cancellationToken);
        }

        return await listFindingsQueryHandler
            .Handle(new ListFindingsQuery(FindingState.Open), cancellationToken)
            .Map(remaining => $"Editor resolved {openCount - remaining.Count} of {openCount} open findings.");
    }
}
