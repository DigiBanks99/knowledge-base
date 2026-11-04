using KnowledgeBase.Agent.Research.Queries;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class ListJudgedResults
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapListJudgedResults()
        {
            endpoints
                .MapGet("topics/{slug}/judged", Handle)
                .WithName(nameof(ListJudgedResults))
                .WithSummary("List a topic's judged results")
                .WithDescription("Returns the results of the topic's recent searches that the Keeper has judged, newest search first, with its probabilities, the author's mark and the thresholds the probabilities are held to.")
                .Produces<JudgedResults>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        string slug,
        ListJudgedResultsQueryHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new ListJudgedResultsQuery(slug), cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError
            }));
}
