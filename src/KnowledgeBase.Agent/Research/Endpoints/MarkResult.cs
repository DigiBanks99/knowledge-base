using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.AspNetCore.Mvc;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class MarkResult
{
    /// <summary>The result to mark, by the search that found it and its URL, and the mark, or null to clear it.</summary>
    public sealed record Body(DateTimeOffset SearchedAt, string Url, ResultMark? Mark);

    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapMarkResult()
        {
            endpoints
                .MapPut("topics/{slug}/results/mark", Handle)
                .WithName(nameof(MarkResult))
                .WithSummary("Mark a judged result")
                .WithDescription("Records the author's judgement of a result the Keeper judged, Useful or Noise, or clears it with a null mark. Marks only feed the Keeper's agreement figures: the verdict and what became of the result stay as they are.")
                .Produces<BatchResult>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        string slug,
        [FromBody] Body body,
        MarkResultCommandHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new MarkResultCommand(slug, body.SearchedAt, body.Url, body.Mark), cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                ResultNotFoundError => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status500InternalServerError
            }));
}
