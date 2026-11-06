using System.ComponentModel;
using System.Net.ServerSentEvents;

using KnowledgeBase.Agent.Conversations;
using KnowledgeBase.Agent.Conversations.Models;
using KnowledgeBase.Agent.Notes.Commands;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace KnowledgeBase.Agent.Mcp;

/// <summary>
/// What Claude can do with the knowledge base through the connector: hand it a note and ask it questions. Failures
/// are thrown as <see cref="McpException"/> so Claude sees them as a tool error it can explain or retry.
/// </summary>
[McpServerToolType]
internal sealed class WikiTools(
    CaptureWikiNoteCommandHandler captureNote,
    ConversationService conversations,
    TimeProvider timeProvider)
{
    private const int MaxQuestionLength = 8_000;

    [McpServerTool(Name = "capture_note", Destructive = false, Idempotent = false)]
    [Description(
        "Saves a note into the user's personal knowledge base. The note is processed later into wiki pages, so it is not " +
        "searchable straight away. Use it when the user asks to remember, save or note something. Write the note as " +
        "self-contained markdown: include the context the user gave, not a summary of this chat.")]
    public async Task<string> CaptureNote(
        [Description("The note, as markdown.")] string content,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new McpException("A note cannot be empty.");
        }

        string name = $"note-{timeProvider.GetUtcNow():yyyy-MM-dd-HHmmss}";
        Result<string, Error> captured = await captureNote.Handle(new CaptureWikiNoteCommand(name, content), cancellationToken);

        return captured.Match(
            filePath => $"Captured as {Path.GetFileName(filePath)}. It will be added to the wiki shortly.",
            error => throw new McpException(error.Message));
    }

    [McpServerTool(Name = "ask_wiki", ReadOnly = true)]
    [Description(
        "Asks the user's personal knowledge base a question. The Librarian answers from the wiki's pages only and says " +
        "when the wiki does not know. Pass the returned conversation id back to ask a follow-up in the same conversation.")]
    public async Task<string> AskWiki(
        [Description("The question, in plain language.")] string question,
        [Description("The conversation id from an earlier answer, to ask a follow-up. Leave out to start a new conversation.")]
        string? conversationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > MaxQuestionLength)
        {
            throw new McpException($"A question must have between 1 and {MaxQuestionLength} characters.");
        }

        string id = string.IsNullOrWhiteSpace(conversationId)
            ? (await conversations.CreateAsync(cancellationToken)).Id
            : conversationId;

        Result<IAsyncEnumerable<SseItem<ConversationStreamEvent>>, Error> asked =
            await conversations.AskAsync(id, question.Trim(), cancellationToken);
        if (asked.IsFailure)
        {
            throw new McpException(asked.Error.Message);
        }

        await foreach (SseItem<ConversationStreamEvent> item in asked.Value.WithCancellation(cancellationToken))
        {
            if (item.Data.Error is { } error)
            {
                throw new McpException(error);
            }

            if (item.Data.Message is { } message)
            {
                return $"{message.Text}\n\n(conversation id: {id})";
            }
        }

        throw new McpException("The Librarian did not answer.");
    }
}
