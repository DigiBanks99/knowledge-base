using System.Text.Json;

namespace KnowledgeBase.Agent.Conversations.Models;

public enum ConversationRole
{
    User,
    Assistant
}

public sealed record ConversationMessage(ConversationRole Role, string Text, DateTimeOffset At);

public sealed record ConversationSummary(string Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record Conversation(
    string Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ConversationMessage> Messages)
{
    public ConversationSummary ToSummary() => new(Id, Title, CreatedAt, UpdatedAt);
}

/// <summary>
/// A conversation as stored on disk: what the UI shows, plus the agent session that carries the conversation's
/// context into the next question. The session never leaves the server.
/// </summary>
internal sealed record StoredConversation(Conversation Conversation, JsonElement? Session);

/// <summary>One event in the stream of an answer: a piece of text, the finished message, or an error.</summary>
public sealed record ConversationStreamEvent(string? Text = null, ConversationMessage? Message = null, string? Error = null);
