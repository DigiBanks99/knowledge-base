using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class ApproveTopicCuration
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapApproveTopicCuration()
        {
            endpoints
                .MapPost("topics/{slug}/approve", Handle)
                .WithName(nameof(ApproveTopicCuration))
                .WithSummary("Approve a topic's curation")
                .WithDescription("Records that the author approves the topic's brief and queries, until they next change. Refused for a topic with no queries or with an open question.")
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
        ApproveTopicCurationCommandHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new ApproveTopicCurationCommand(slug), cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                TopicNotFoundError => StatusCodes.Status404NotFound,
                TopicNeedsReviewError or TopicNotCuratedError => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            }));
}
