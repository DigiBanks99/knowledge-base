using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.AspNetCore.Mvc;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class ClarifyTopic
{
    /// <summary>The author's answer to the Pleb's question.</summary>
    public sealed record Body(string? Answer);

    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapClarifyTopic()
        {
            endpoints
                .MapPost("topics/{slug}/clarify", Handle)
                .WithName(nameof(ClarifyTopic))
                .WithSummary("Answer the Pleb's question about a topic")
                .WithDescription("Stores the answer as guidance for every later curation of the topic and queues a curation ahead of those page changes requested. The topic stays unsearched until the Pleb sets new queries. Refused for a topic without an open question.")
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
        ClarifyTopicCommandHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new ClarifyTopicCommand(slug, body.Answer), cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                TopicNotFoundError => StatusCodes.Status404NotFound,
                TopicNotInReviewError => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            }));
}
