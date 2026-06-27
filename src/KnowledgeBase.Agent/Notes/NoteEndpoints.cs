using KnowledgeBase.Agent.Notes.Endpoints;

namespace KnowledgeBase.Agent.Notes;

internal static class NotesEndpoints
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapNotesEndpoints()
        {
            endpoints
                .MapGroup("notes")
                .WithTags("wiki", "notes")
                .MapCaptureNote()
                .MapListNotes();

            return endpoints;
        }
    }
}