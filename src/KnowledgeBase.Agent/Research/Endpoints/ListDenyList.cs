using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Queries;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class ListDenyList
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapListDenyList()
        {
            endpoints
                .MapGet("deny-list", Handle)
                .WithName(nameof(ListDenyList))
                .WithSummary("List the deny-list")
                .WithDescription("Returns every name kept out of search queries, with its kind, who decided it and whether it is denied or allowed.")
                .Produces<IReadOnlyList<DenyListEntry>>()
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        ListDenyListQueryHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new ListDenyListQuery(), cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: StatusCodes.Status500InternalServerError));
}