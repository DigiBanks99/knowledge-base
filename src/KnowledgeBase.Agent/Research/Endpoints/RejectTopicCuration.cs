using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.AspNetCore.Mvc;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class RejectTopicCuration
{
    /// <summary>Why the curation is wrong, if the author says.</summary>
    public sealed record Body(string? Reason);

    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapRejectTopicCuration()
        {
            endpoints
                .MapPost("topics/{slug}/reject", Handle)
                .WithName(nameof(RejectTopicCuration))
                .WithSummary("Reject a topic's curation")
                .WithDescription("Marks the topic's brief and queries invalid. The rejected curation and the optional reason are stored as guidance for every later curation, and a curation is queued ahead of those page changes requested. Refused for a topic with no queries yet.")
                .Produces<ResearchTopic>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status409Conflict)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        string slug,
        [FromBody] Body body,
        RejectTopicCurationCommandHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new RejectTopicCurationCommand(slug, body.Reason), cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                TopicNotFoundError => StatusCodes.Status404NotFound,
                TopicNotCuratedError => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            }));
}
