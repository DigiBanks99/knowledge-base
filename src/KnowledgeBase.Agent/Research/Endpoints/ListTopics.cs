using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Queries;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class ListTopics
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapListTopics()
        {
            endpoints
                .MapGet("topics", Handle)
                .WithName(nameof(ListTopics))
                .WithSummary("List the research topics")
                .WithDescription("Returns every topic derived from the wiki, with its state, brief, queries and whether the author muted it.")
                .Produces<IReadOnlyList<TopicSummary>>()
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        ListTopicsQueryHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new ListTopicsQuery(), cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: StatusCodes.Status500InternalServerError));
}
