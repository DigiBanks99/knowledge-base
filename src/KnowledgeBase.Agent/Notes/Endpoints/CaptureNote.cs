using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Notes.Commands;
using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Commands;

namespace KnowledgeBase.Agent.Notes.Endpoints;

public sealed record CaptureNoteRequest(string Content);
public sealed record CaptureNoteRequestRecorded(string FilePath);

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
                .Produces<CaptureNoteRequestRecorded>(StatusCodes.Status201Created)
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
                filePath => Results.Created(string.Empty, new CaptureNoteRequestRecorded(filePath)),
                error => Results.Problem(error.Message, statusCode: error switch
                {
                    ValidationError => StatusCodes.Status400BadRequest,
                    FileConflictError => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status500InternalServerError
                }));
    }
}