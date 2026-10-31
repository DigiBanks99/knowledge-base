using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class RecurateTopic
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapRecurateTopic()
        {
            endpoints
                .MapPost("topics/{slug}/recurate", Handle)
                .WithName(nameof(RecurateTopic))
                .WithSummary("Curate a topic again")
                .WithDescription("Queues a curation of the topic by the Pleb, ahead of those page changes requested, without a verdict on the current one. Unpins a topic whose brief and queries the author edited.")
                .Produces<ResearchTopic>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        string slug,
        RecurateTopicCommandHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new RecurateTopicCommand(slug), cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                TopicNotFoundError => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status500InternalServerError
            }));
}
