using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.AspNetCore.Mvc;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class SetDenyListEntryState
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapSetDenyListEntryState()
        {
            // The name travels in the body rather than the path, as names hold spaces and punctuation.
            endpoints
                .MapPut("deny-list/state", Handle)
                .WithName(nameof(SetDenyListEntryState))
                .WithSummary("Deny or allow a name")
                .WithDescription("Sets whether a deny-list entry is kept out of search queries. The decision is the author's from then on: classification never changes it again.")
                .Produces<DenyListEntry>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        [FromBody] SetDenyListEntryStateCommand request,
        SetDenyListEntryStateCommandHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(request, cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                DenyListEntryNotFoundError => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status500InternalServerError
            }));
}