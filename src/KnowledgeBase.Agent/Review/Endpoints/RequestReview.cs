using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Review.Commands;

using Microsoft.AspNetCore.Mvc;

using OpenTelemetry.Trace;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Review.Endpoints;

public sealed record ReviewRequestRecorded(string FilePath);

internal static class RequestReview
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapRequestReview()
        {
            endpoints.MapPost(string.Empty, Handle)
                .WithName(nameof(RequestReview))
                .WithSummary("Request a review of the wiki")
                .WithDescription("Queues a new review request for the wiki")
                .Produces<ReviewRequestRecorded>(StatusCodes.Status201Created)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        [FromBody] RequestReviewWikiCommand request,
        RequestReviewWikiCommandHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(request, cancellationToken)
        .Match(
            filePath => Results.Created(string.Empty, new ReviewRequestRecorded(filePath)),
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                FileConflictError => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            }));
}
