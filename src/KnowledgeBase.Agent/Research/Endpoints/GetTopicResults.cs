using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Queries;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class GetTopicResults
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapGetTopicResults()
        {
            endpoints
                .MapGet("topics/{slug}/results", Handle)
                .WithName(nameof(GetTopicResults))
                .WithSummary("Get a topic's pending results")
                .WithDescription("Returns the results of the topic's last search that are waiting to be triaged, with the query that found each. Not found when none are waiting.")
                .Produces<ResearchBatch>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        string slug,
        GetTopicResultsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        Result<Maybe<ResearchBatch>, Error> batch = await handler.Handle(new GetTopicResultsQuery(slug), cancellationToken);
        if (batch.IsFailure)
        {
            return Results.Problem(batch.Error.Message, statusCode: batch.Error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError
            });
        }

        return batch.Value.HasValue
            ? Results.Ok(batch.Value.Value)
            : Results.Problem($"No results of '{slug}' are waiting to be triaged.", statusCode: StatusCodes.Status404NotFound);
    }
}
