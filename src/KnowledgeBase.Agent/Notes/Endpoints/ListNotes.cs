using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Notes.Queries;
using KnowledgeBase.Agent.Wiki.Errors;

namespace KnowledgeBase.Agent.Notes.Endpoints;

internal static class ListNotes
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapListNotes()
        {
            endpoints
                .MapGet(string.Empty, Handle)
                .WithName(nameof(ListNotes))
                .WithSummary("List wiki notes")
                .WithDescription("Returns a list of all wiki notes that are to be processed")
                .Produces<Models.Notes>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static Microsoft.AspNetCore.Http.IResult Handle(ListWikiNotesQueryHandler handler) => handler
        .Handle(new ListWikiNotesQuery())
        .Match(
            notes => Results.Ok(notes),
            error => Results.Problem(error.Message, statusCode: error switch
            {
                DirectoryNotFoundError => StatusCodes.Status404NotFound,
                DirectoryReadError => StatusCodes.Status500InternalServerError,
                _ => StatusCodes.Status500InternalServerError
            }));
}