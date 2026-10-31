using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Search;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class ResearchTopicNow
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapResearchTopicNow()
        {
            endpoints
                .MapPost("topics/{slug}/research", Handle)
                .WithName(nameof(ResearchTopicNow))
                .WithSummary("Research a topic now")
                .WithDescription("Searches the topic's queries now, whether or not it is due, and keeps the new results for triage. Refused for a muted topic, one waiting for the author to answer the Pleb's question, one the Pleb has not curated yet and one whose last results are still waiting.")
                .Produces<TopicSearchOutcome>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status409Conflict)
                .Produces(StatusCodes.Status502BadGateway)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        string slug,
        ResearchTopicNowCommandHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new ResearchTopicNowCommand(slug), cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                TopicNotFoundError => StatusCodes.Status404NotFound,
                TopicMutedError or TopicNeedsReviewError or TopicNotCuratedError or BatchInFlightError or TopicBeingSearchedError => StatusCodes.Status409Conflict,
                SearchSourceError or AggregateError => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status500InternalServerError
            }));
}
