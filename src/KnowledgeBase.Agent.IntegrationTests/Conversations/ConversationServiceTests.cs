using KnowledgeBase.Agent.Conversations;
using KnowledgeBase.Agent.Conversations.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Conversations;

public sealed class ConversationServiceTests : IAsyncDisposable
{
    private readonly TestWiki _wiki = new();
    private readonly ConversationService _conversations;

    public ConversationServiceTests()
    {
        _conversations = _wiki.Get<ConversationService>();
    }

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task AConversationIsStarted()
    {
        await _wiki.InitialiseAsync();

        Conversation started = await _conversations.CreateAsync(CancellationToken.None);
        Conversation? stored = await _conversations.GetAsync(started.Id, CancellationToken.None);
        IReadOnlyList<ConversationSummary> listed = await _conversations.ListAsync(CancellationToken.None);

        ItShouldStartEmpty();
        ItShouldKeepIt();
        ItShouldListIt();

        void ItShouldStartEmpty() => started.ShouldSatisfyAllConditions(
            conversation => conversation.Title.ShouldBe("New conversation"),
            conversation => conversation.Messages.ShouldBeEmpty());

        void ItShouldKeepIt() => stored.ShouldNotBeNull().Id.ShouldBe(started.Id);

        void ItShouldListIt() => listed.ShouldHaveSingleItem().ShouldBe(started.ToSummary());
    }

    [Test]
    public async Task AConversationIsDeleted()
    {
        await _wiki.InitialiseAsync();
        Conversation started = await _conversations.CreateAsync(CancellationToken.None);

        bool deleted = _conversations.Delete(started.Id);
        Conversation? afterDeleting = await _conversations.GetAsync(started.Id, CancellationToken.None);
        bool deletedAgain = _conversations.Delete(started.Id);

        ItShouldDeleteIt();
        ItShouldBeGone();
        ItShouldNotDeleteItTwice();

        void ItShouldDeleteIt() => deleted.ShouldBeTrue();

        void ItShouldBeGone() => afterDeleting.ShouldBeNull();

        void ItShouldNotDeleteItTwice() => deletedAgain.ShouldBeFalse();
    }

    [Test]
    [Arguments("../runs/20261024-101500-factfinder-0123456789")]
    [Arguments("not-a-conversation-id")]
    [Arguments("")]
    public async Task AnUnsafeIdIsUsed(string id)
    {
        await _wiki.InitialiseAsync();

        Conversation? found = await _conversations.GetAsync(id, CancellationToken.None);
        bool deleted = _conversations.Delete(id);

        ItShouldFindNothing();
        ItShouldDeleteNothing();

        void ItShouldFindNothing() => found.ShouldBeNull();

        void ItShouldDeleteNothing() => deleted.ShouldBeFalse();
    }

    [Test]
    public async Task AQuestionIsAskedInAConversationThatDoesNotExist()
    {
        await _wiki.InitialiseAsync();

        Result<IAsyncEnumerable<System.Net.ServerSentEvents.SseItem<ConversationStreamEvent>>, Error> answer =
            await _conversations.AskAsync(Guid.NewGuid().ToString("N"), "Who is Susan?", CancellationToken.None);

        ItShouldRefuseTheQuestion();
        ItShouldNotAskTheLibrarian();

        void ItShouldRefuseTheQuestion() => answer.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("ConversationNotFound");

        void ItShouldNotAskTheLibrarian() => _wiki.Librarian.Prompts.ShouldBeEmpty();
    }
}