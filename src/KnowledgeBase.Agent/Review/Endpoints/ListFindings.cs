using System.ComponentModel;

using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Review.Models;
using KnowledgeBase.Agent.Review.Queries;

using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBase.Agent.Review.Endpoints;

internal static class ListFindings
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapListFindings()
        {
            endpoints
                .MapGet(string.Empty, Handle)
                .WithName(nameof(ListFindings))
                .WithSummary("List wiki findings")
                .WithDescription("Returns the wiki findings in the given state, or every finding when no state is given.")
                .Produces<IReadOnlyCollection<ReviewFinding>>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<Microsoft.AspNetCore.Http.IResult> Handle(
        [FromQuery] [Description("Filter the findings by state. If not specified, all findings will be returned.")]
        FindingState? state,
        ListFindingsQueryHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new ListFindingsQuery(state), cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                DirectoryNotFoundError => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status500InternalServerError
            }));
}
