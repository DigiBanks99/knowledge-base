using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.AspNetCore.Mvc;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class AddDenyListEntry
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapAddDenyListEntry()
        {
            endpoints
                .MapPost("deny-list", Handle)
                .WithName(nameof(AddDenyListEntry))
                .WithSummary("Deny or allow a name ahead of classification")
                .WithDescription("Adds a name to the deny-list as denied or allowed, or sets an existing entry's kind and state. The decision is the author's from then on: classification never changes it again.")
                .Produces<DenyListEntry>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        [FromBody] AddDenyListEntryCommand request,
        AddDenyListEntryCommandHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(request, cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError
            }));
}
