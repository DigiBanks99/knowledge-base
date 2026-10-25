using System.Net;
using System.Net.Http.Json;

using KnowledgeBase.Agent.Conversations;
using KnowledgeBase.Agent.Conversations.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

public sealed class ConversationsApiTests : IDisposable
{
    private readonly TestApi _api = new();

    public void Dispose() => _api.Dispose();

    [Test]
    public async Task AConversationIsStarted()
    {
        HttpClient client = _api.Client();

        HttpResponseMessage started = await client.PostAsync("/api/conversations", null);
        Conversation conversation = await TestApi.ReadAsync<Conversation>(started);
        HttpResponseMessage fetched = await client.GetAsync($"/api/conversations/{conversation.Id}");

        ItShouldStartIt();
        ItShouldPointToIt();

        void ItShouldStartIt() => started.StatusCode.ShouldBe(HttpStatusCode.Created);

        void ItShouldPointToIt()
        {
            started.Headers.Location.ShouldBe(new Uri($"/api/conversations/{conversation.Id}", UriKind.Relative));
            fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task AConversationIsDeleted()
    {
        HttpClient client = _api.Client();
        Conversation conversation = await TestApi.ReadAsync<Conversation>(await client.PostAsync("/api/conversations", null));

        HttpResponseMessage deleted = await client.DeleteAsync($"/api/conversations/{conversation.Id}");
        HttpResponseMessage fetched = await client.GetAsync($"/api/conversations/{conversation.Id}");
        HttpResponseMessage deletedAgain = await client.DeleteAsync($"/api/conversations/{conversation.Id}");

        ItShouldDeleteIt();
        ItShouldBeGone();

        void ItShouldDeleteIt() => deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        void ItShouldBeGone()
        {
            fetched.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            deletedAgain.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(8_001)]
    public async Task AQuestionOfTheWrongLengthIsAsked(int length)
    {
        HttpClient client = _api.Client();
        Conversation conversation = await TestApi.ReadAsync<Conversation>(await client.PostAsync("/api/conversations", null));

        HttpResponseMessage asked = await client.PostAsJsonAsync(
            $"/api/conversations/{conversation.Id}/messages",
            new AskQuestionRequest(new string('a', length)),
            TestApi.Json);

        ItShouldRefuseTheQuestion();
        ItShouldNotAskTheLibrarian();

        void ItShouldRefuseTheQuestion() => asked.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        void ItShouldNotAskTheLibrarian() => _api.Agent(KnownAgents.Librarian).Prompts.ShouldBeEmpty();
    }

    [Test]
    public async Task AQuestionIsAskedInAConversationThatDoesNotExist()
    {
        HttpResponseMessage asked = await _api.Client().PostAsJsonAsync(
            $"/api/conversations/{Guid.NewGuid():N}/messages",
            new AskQuestionRequest("Who is Susan?"),
            TestApi.Json);

        ItShouldNotFindTheConversation();

        void ItShouldNotFindTheConversation() => asked.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
