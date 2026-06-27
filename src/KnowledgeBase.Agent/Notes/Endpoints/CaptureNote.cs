using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Commands;

namespace KnowledgeBase.Agent.Notes.Endpoints;

public record CaptureNoteRequest(string Content);

internal static class CaptureNote
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapCaptureNote()
        {
            endpoints
                .MapPost("note", Handle)
                .WithName(nameof(CaptureNote))
                .WithSummary("Capture Note")
                .WithDescription("Capture a note and store it in the file system.")
                .Produces<string>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status409Conflict)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<Microsoft.AspNetCore.Http.IResult> Handle(
        CaptureNoteRequest request,
        TimeProvider timeProvider,
        CaptureWikiNoteCommandHandler handler)
    {
        string name = $"note-{timeProvider.GetUtcNow():yyyy-MM-dd-HHmmss}";

        CaptureWikiNoteCommand createWikiSourceCommand = new(name, request.Content);
        return await handler
            .Handle(createWikiSourceCommand, CancellationToken.None)
            .Match(
                filePath => Results.Ok(new { FilePath = filePath }),
                error => Results.Problem(error.Message, statusCode: error switch
                {
                    ValidationError => StatusCodes.Status400BadRequest,
                    FileConflictError => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status500InternalServerError
                }));
    }
}