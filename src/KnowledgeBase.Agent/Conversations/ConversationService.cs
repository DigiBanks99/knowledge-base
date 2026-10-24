using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using KnowledgeBase.Agent.Common;
using KnowledgeBase.Agent.Conversations.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Conversations;

public sealed class ConversationBusyError(string id)
    : Error("ConversationBusy", $"Conversation '{id}' is still answering the previous question.");

/// <summary>
/// Multi-turn conversations with the Librarian. Each conversation keeps its agent session on disk, so follow-up
/// questions keep their context across app restarts.
/// </summary>
internal sealed class ConversationService(
    ILogger<ConversationService> logger,
    IOptions<WikiConfigurationSettings> options,
    [FromKeyedServices(KnownAgents.Librarian)] AIAgent librarian,
    TimeProvider timeProvider)
{
    private const int TitleLength = 60;

    private readonly WikiConfigurationSettings _settings = options.Value;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _conversationLocks = new();

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredConversation> conversations =
            await JsonFileStore.ReadAllAsync<StoredConversation>(_settings.ConversationsDirectory, cancellationToken);
        return conversations
            .Select(stored => stored.Conversation.ToSummary())
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .ToList();
    }

    public async Task<Conversation?> GetAsync(string id, CancellationToken cancellationToken) =>
        (await ReadAsync(id, cancellationToken))?.Conversation;

    public async Task<Conversation> CreateAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        Conversation conversation = new(Guid.NewGuid().ToString("N"), "New conversation", now, now, []);
        await JsonFileStore.WriteAsync(ConversationPath(conversation.Id), new StoredConversation(conversation, null), cancellationToken);
        return conversation;
    }

    public bool Delete(string id)
    {
        if (!IsValidId(id) || !File.Exists(ConversationPath(id)))
        {
            return false;
        }

        File.Delete(ConversationPath(id));
        _conversationLocks.TryRemove(id, out _);
        return true;
    }

    /// <summary>
    /// Asks the Librarian a question within a conversation and streams the answer as it is written. Fails with
    /// <see cref="ConversationBusyError"/> if the conversation is still answering a previous question.
    /// </summary>
    public async Task<Result<IAsyncEnumerable<SseItem<ConversationStreamEvent>>, Error>> AskAsync(
        string id,
        string question,
        CancellationToken cancellationToken)
    {
        StoredConversation? stored = await ReadAsync(id, cancellationToken);
        if (stored is null)
        {
            return new ValidationError("ConversationNotFound", $"Conversation '{id}' was not found.");
        }

        SemaphoreSlim conversationLock = _conversationLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        if (!await conversationLock.WaitAsync(0, cancellationToken))
        {
            return new ConversationBusyError(id);
        }

        return Result.Success<IAsyncEnumerable<SseItem<ConversationStreamEvent>>, Error>(
            AnswerAsync(stored, question, conversationLock, cancellationToken));
    }

    private async IAsyncEnumerable<SseItem<ConversationStreamEvent>> AnswerAsync(
        StoredConversation stored,
        string question,
        SemaphoreSlim conversationLock,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            AgentSession session = stored.Session is { } sessionState
                ? await librarian.DeserializeSessionAsync(sessionState, cancellationToken: cancellationToken)
                : await librarian.CreateSessionAsync(cancellationToken);

            ConversationMessage questionMessage = new(ConversationRole.User, question, timeProvider.GetUtcNow());
            StringBuilder answer = new();
            string? error = null;

            await using (IAsyncEnumerator<AgentResponseUpdate> updates = librarian
                .RunStreamingAsync(question, session, cancellationToken: cancellationToken)
                .GetAsyncEnumerator(cancellationToken))
            {
                while (true)
                {
                    string? text;
                    try
                    {
                        if (!await updates.MoveNextAsync())
                        {
                            break;
                        }

                        text = updates.Current.Text;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LibrarianFailed(stored.Conversation.Id, ex);
                        error = "The Librarian could not answer. Please try again.";
                        break;
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        answer.Append(text);
                        yield return new SseItem<ConversationStreamEvent>(new ConversationStreamEvent(Text: text), "delta");
                    }
                }
            }

            if (error is not null)
            {
                yield return new SseItem<ConversationStreamEvent>(new ConversationStreamEvent(Error: error), "error");
                yield break;
            }

            ConversationMessage answerMessage = new(ConversationRole.Assistant, answer.ToString(), timeProvider.GetUtcNow());
            Conversation conversation = stored.Conversation with
            {
                Title = stored.Conversation.Messages.Count == 0 ? Title(question) : stored.Conversation.Title,
                UpdatedAt = answerMessage.At,
                Messages = [.. stored.Conversation.Messages, questionMessage, answerMessage]
            };
            JsonElement sessionStateAfter = await librarian.SerializeSessionAsync(session, cancellationToken: cancellationToken);
            await JsonFileStore.WriteAsync(
                ConversationPath(conversation.Id),
                new StoredConversation(conversation, sessionStateAfter),
                cancellationToken);

            yield return new SseItem<ConversationStreamEvent>(new ConversationStreamEvent(Message: answerMessage), "done");
        }
        finally
        {
            conversationLock.Release();
        }
    }

    private async Task<StoredConversation?> ReadAsync(string id, CancellationToken cancellationToken) =>
        IsValidId(id)
            ? await JsonFileStore.ReadAsync<StoredConversation>(ConversationPath(id), cancellationToken)
            : null;

    private string ConversationPath(string id) => Path.Combine(_settings.ConversationsDirectory, $"{id}.json");

    private static bool IsValidId(string id) => id.Length == 32 && id.All(char.IsAsciiHexDigit);

    private static string Title(string question)
    {
        string singleLine = string.Join(' ', question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length > TitleLength ? $"{singleLine[..TitleLength].TrimEnd()}…" : singleLine;
    }
}