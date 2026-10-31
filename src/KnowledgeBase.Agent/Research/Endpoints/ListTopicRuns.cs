using System.ComponentModel;

using KnowledgeBase.Agent.Research.Queries;

using Microsoft.AspNetCore.Mvc;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class ListTopicRuns
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapListTopicRuns()
        {
            endpoints
                .MapGet("topics/{slug}/runs", Handle)
                .WithName(nameof(ListTopicRuns))
                .WithSummary("List a topic's runs")
                .WithDescription($"Returns one page of the agent runs recorded for the topic, {ListTopicRunsQueryHandler.PageSize} at a time, newest first, and whether older runs exist.")
                .Produces<TopicRunsPage>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        string slug,
        [FromQuery, Description("The page, counted from 1 for the newest runs.")] int? page,
        ListTopicRunsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        Result<TopicRunsPage, Error> runs = await handler.Handle(new ListTopicRunsQuery(slug, page ?? 1), cancellationToken);
        return runs.IsSuccess
            ? Results.Ok(runs.Value)
            : Results.Problem(runs.Error.Message, statusCode: runs.Error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError
            });
    }
}
