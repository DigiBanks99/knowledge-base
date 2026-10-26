using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.AspNetCore.Mvc;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class SetTopicMuted
{
    /// <summary>Whether research for the topic is stopped.</summary>
    public sealed record Body(bool Muted);

    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapSetTopicMuted()
        {
            endpoints
                .MapPut("topics/{slug}/muted", Handle)
                .WithName(nameof(SetTopicMuted))
                .WithSummary("Mute or unmute a topic")
                .WithDescription("Stops or resumes research for a topic without moving it in the wiki. The slug is the topic's path with '/' replaced by '--', as the topic list returns it.")
                .Produces<ResearchTopic>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        string slug,
        [FromBody] Body body,
        SetTopicMutedCommandHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new SetTopicMutedCommand(slug, body.Muted), cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                TopicNotFoundError => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status500InternalServerError
            }));
}
