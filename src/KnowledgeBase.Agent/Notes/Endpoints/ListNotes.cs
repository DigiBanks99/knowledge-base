using KnowledgeBase.Agent.Notes.Queries;

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
                .WithSummary("List notes")
                .WithDescription("Returns every captured note and where it is in the pipeline, newest first.")
                .Produces<IReadOnlyList<NoteSummary>>();

            endpoints
                .MapGet("{name}", Get)
                .WithName("GetNote")
                .WithSummary("Get a note")
                .Produces<NoteDetail>()
                .Produces(StatusCodes.Status404NotFound);

            return endpoints;
        }
    }

    private static Task<IReadOnlyList<NoteSummary>> Handle(NoteCatalog catalog, CancellationToken cancellationToken) =>
        catalog.ListAsync(cancellationToken);

    private static async Task<Microsoft.AspNetCore.Http.IResult> Get(
        string name,
        NoteCatalog catalog,
        CancellationToken cancellationToken) =>
        await catalog.GetAsync(name, cancellationToken) is { } note
            ? Results.Ok(note)
            : Results.NotFound();
}
