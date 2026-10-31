using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.AspNetCore.Mvc;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class EditTopicCuration
{
    /// <summary>The author's brief and queries for the topic.</summary>
    public sealed record Body(string? Brief, IReadOnlyList<string>? Queries);

    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapEditTopicCuration()
        {
            endpoints
                .MapPut("topics/{slug}/curation", Handle)
                .WithName(nameof(EditTopicCuration))
                .WithSummary("Set a topic's brief and queries")
                .WithDescription("Sets the topic's brief and search queries by hand, checked against the deny-list as the Pleb's are. The topic is pinned: page changes no longer request curation for it until it is recurated.")
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
        EditTopicCurationCommandHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new EditTopicCurationCommand(slug, body.Brief, body.Queries), cancellationToken)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                TopicNotFoundError => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status500InternalServerError
            }));
}
