using System.Net.ServerSentEvents;

using KnowledgeBase.Agent.Conversations.Models;

namespace KnowledgeBase.Agent.Conversations;

public sealed record AskQuestionRequest(string Text);

internal static class ConversationEndpoints
{
    private const int MaxQuestionLength = 8_000;

    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapConversationEndpoints()
        {
            RouteGroupBuilder conversations = endpoints
                .MapGroup("conversations")
                .WithTags("conversations");

            conversations.MapGet(string.Empty, List)
                .WithName("ListConversations")
                .WithSummary("List conversations")
                .Produces<IReadOnlyList<ConversationSummary>>();

            conversations.MapPost(string.Empty, Create)
                .WithName("CreateConversation")
                .WithSummary("Start a conversation")
                .Produces<Conversation>(StatusCodes.Status201Created);

            conversations.MapGet("{id}", Get)
                .WithName("GetConversation")
                .WithSummary("Get a conversation")
                .Produces<Conversation>()
                .Produces(StatusCodes.Status404NotFound);

            conversations.MapDelete("{id}", Delete)
                .WithName("DeleteConversation")
                .WithSummary("Delete a conversation")
                .Produces(StatusCodes.Status204NoContent)
                .Produces(StatusCodes.Status404NotFound);

            conversations.MapPost("{id}/messages", Ask)
                .WithName("AskQuestion")
                .WithSummary("Ask the Librarian a question")
                .WithDescription("Streams the answer as server-sent events: 'delta' events with text, then a 'done' event with the full message, or an 'error' event.")
                .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status409Conflict);

            return endpoints;
        }
    }

    private static Task<IReadOnlyList<ConversationSummary>> List(ConversationService service, CancellationToken cancellationToken) =>
        service.ListAsync(cancellationToken);

    private static async Task<Microsoft.AspNetCore.Http.IResult> Create(ConversationService service, CancellationToken cancellationToken)
    {
        Conversation conversation = await service.CreateAsync(cancellationToken);
        return Results.Created($"/api/conversations/{conversation.Id}", conversation);
    }

    private static async Task<Microsoft.AspNetCore.Http.IResult> Get(string id, ConversationService service, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken) is { } conversation
            ? Results.Ok(conversation)
            : Results.NotFound();

    private static Microsoft.AspNetCore.Http.IResult Delete(string id, ConversationService service) =>
        service.Delete(id) ? Results.NoContent() : Results.NotFound();

    private static async Task<Microsoft.AspNetCore.Http.IResult> Ask(
        string id,
        AskQuestionRequest request,
        ConversationService service,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaxQuestionLength)
        {
            return Results.Problem($"A question must have between 1 and {MaxQuestionLength} characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        Result<IAsyncEnumerable<SseItem<ConversationStreamEvent>>, Error> answer =
            await service.AskAsync(id, request.Text.Trim(), cancellationToken);
        return answer.Match(
            TypedResults.ServerSentEvents,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ConversationBusyError => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status404NotFound
            }));
    }
}