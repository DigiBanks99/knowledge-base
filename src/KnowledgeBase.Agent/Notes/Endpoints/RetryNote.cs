using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Notes.Queries;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Notes.Endpoints;

internal static class RetryNote
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapRetryNote()
        {
            endpoints
                .MapPost("{name}/retry", Handle)
                .WithName(nameof(RetryNote))
                .WithSummary("Retry a failed note")
                .WithDescription("Moves a failed note back to new notes, so it is processed again.")
                .Produces(StatusCodes.Status202Accepted)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status409Conflict);

            return endpoints;
        }
    }

    private static Microsoft.AspNetCore.Http.IResult Handle(
        string name,
        NoteCatalog catalog,
        MoveFileHandler moveFileHandler,
        IOptions<WikiConfigurationSettings> settings)
    {
        if (catalog.Find(name) is not (NoteState state, string filePath))
        {
            return Results.NotFound();
        }

        if (state != NoteState.Failed)
        {
            return Results.Problem($"Only failed notes can be retried; this note is {state}.", statusCode: StatusCodes.Status409Conflict);
        }

        return moveFileHandler
            .Handle(new MoveFileRequest(filePath, Path.Combine(settings.Value.NewNotesDirectory, name)))
            .Match(
                _ => Results.Accepted(),
                error => Results.Problem(error.Message, statusCode: StatusCodes.Status500InternalServerError));
    }
}