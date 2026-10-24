using System.Net;
using System.Net.Http.Json;

using KnowledgeBase.Agent.Auth;
using KnowledgeBase.Agent.Notes.Endpoints;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

/// <summary>Everything under <c>/api</c> needs a signed-in, allowed user, and state changes need the CSRF header.</summary>
public sealed class AuthTests : IDisposable
{
    private readonly TestApi _api = new();

    public void Dispose() => _api.Dispose();

    [Test]
    public async Task SomeoneWhoIsNotSignedInCallsTheApi()
    {
        HttpResponseMessage response = await _api.Client(objectId: null).GetAsync("/api/notes");

        ItShouldAskThemToSignIn();

        void ItShouldAskThemToSignIn() => response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task SomeoneWhoIsNotAllowedInCallsTheApi()
    {
        HttpResponseMessage response = await _api.Client(objectId: "a-stranger").GetAsync("/api/notes");

        ItShouldTurnThemAway();

        void ItShouldTurnThemAway() => response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TheOwnerCallsTheApi()
    {
        HttpResponseMessage response = await _api.Client().GetAsync("/api/me");

        ItShouldLetThemIn();
        await ItShouldKnowWhoTheyAre();

        void ItShouldLetThemIn() => response.StatusCode.ShouldBe(HttpStatusCode.OK);

        async Task ItShouldKnowWhoTheyAre() => (await TestApi.ReadAsync<CurrentUser>(response)).Name.ShouldBe("Test User");
    }

    [Test]
    public async Task AChangeIsSentWithoutTheCsrfHeader()
    {
        HttpResponseMessage response = await _api.Client(sendsCsrfHeader: false)
            .PostAsJsonAsync("/api/notes", new CaptureNoteRequest("A note."), TestApi.Json);

        ItShouldRefuseTheChange();
        ItShouldNotCaptureTheNote();

        void ItShouldRefuseTheChange() => response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        void ItShouldNotCaptureTheNote() => TestWiki.FileNames(_api.Settings.NewNotesDirectory).ShouldBeEmpty();
    }

    [Test]
    public async Task AReadIsSentWithoutTheCsrfHeader()
    {
        HttpResponseMessage response = await _api.Client(sendsCsrfHeader: false).GetAsync("/api/notes");

        ItShouldAnswer();

        void ItShouldAnswer() => response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task TheLivenessIsCheckedWithoutSigningIn()
    {
        HttpResponseMessage response = await _api.Client(objectId: null).GetAsync("/alive");

        ItShouldAnswer();

        void ItShouldAnswer() => response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}