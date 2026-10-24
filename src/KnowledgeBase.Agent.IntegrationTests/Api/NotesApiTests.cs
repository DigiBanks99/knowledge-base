using System.Net;
using System.Net.Http.Json;

using KnowledgeBase.Agent.Notes.Endpoints;
using KnowledgeBase.Agent.Notes.Queries;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

public sealed class NotesApiTests : IDisposable
{
    private const string NoteName = "note-2026-10-24-101500.md";

    private readonly TestApi _api = new();

    public void Dispose() => _api.Dispose();

    [Test]
    public async Task ANoteIsCaptured()
    {
        HttpResponseMessage response = await _api.Client()
            .PostAsJsonAsync("/api/notes", new CaptureNoteRequest("Susan gave feedback."), TestApi.Json);
        CaptureNoteRequestRecorded recorded = await TestApi.ReadAsync<CaptureNoteRequestRecorded>(response);
        bool processed = await Eventually.Until(() => _api.Agent(KnownAgents.FactFinder).Prompts.Count == 1);

        ItShouldRecordIt();
        ItShouldPointToTheNote();
        ItShouldStartProcessingIt();

        void ItShouldRecordIt() => response.StatusCode.ShouldBe(HttpStatusCode.Created);

        void ItShouldPointToTheNote()
        {
            recorded.Name.ShouldMatch(@"^note-\d{4}-\d{2}-\d{2}-\d{6}\.md$");
            response.Headers.Location.ShouldBe(new Uri($"/api/notes/{recorded.Name}", UriKind.Relative));
        }

        void ItShouldStartProcessingIt()
        {
            processed.ShouldBeTrue();
            _api.Agent(KnownAgents.FactFinder).Prompts.Single().ShouldContain("Susan gave feedback.");
        }
    }

    [Test]
    public async Task AnEmptyNoteIsCaptured()
    {
        HttpResponseMessage response = await _api.Client()
            .PostAsJsonAsync("/api/notes", new CaptureNoteRequest("  "), TestApi.Json);

        ItShouldRefuseIt();

        void ItShouldRefuseIt() => response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ANoteIsRequested()
    {
        Write(_api.Settings.ProcessedNotesDirectory, "Woven into the wiki.");

        HttpResponseMessage response = await _api.Client().GetAsync($"/api/notes/{NoteName}");

        ItShouldFindIt();
        await ItShouldReturnItsContentAndState();

        void ItShouldFindIt() => response.StatusCode.ShouldBe(HttpStatusCode.OK);

        async Task ItShouldReturnItsContentAndState()
        {
            NoteDetail note = await TestApi.ReadAsync<NoteDetail>(response);
            note.Content.ShouldBe("Woven into the wiki.");
            note.Summary.State.ShouldBe(NoteState.Processed);
        }
    }

    [Test]
    public async Task AFailedNoteIsRetried()
    {
        Write(_api.Settings.FailedNotesDirectory, "Try me again.");

        HttpResponseMessage response = await _api.Client().PostAsync($"/api/notes/{NoteName}/retry", null);
        bool processedAgain = await Eventually.Until(() => _api.Agent(KnownAgents.FactFinder).Prompts.Count == 1);

        ItShouldAcceptTheRetry();
        ItShouldProcessTheNoteAgain();

        void ItShouldAcceptTheRetry() => response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        void ItShouldProcessTheNoteAgain()
        {
            processedAgain.ShouldBeTrue();
            _api.Agent(KnownAgents.FactFinder).Prompts.Single().ShouldContain("Try me again.");
        }
    }

    [Test]
    public async Task AProcessedNoteIsRetried()
    {
        Write(_api.Settings.ProcessedNotesDirectory, "Already woven.");

        HttpResponseMessage response = await _api.Client().PostAsync($"/api/notes/{NoteName}/retry", null);

        ItShouldRefuseTheRetry();
        ItShouldLeaveTheNoteWhereItIs();

        void ItShouldRefuseTheRetry() => response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        void ItShouldLeaveTheNoteWhereItIs() => TestWiki.FileNames(_api.Settings.ProcessedNotesDirectory).ShouldBe([NoteName]);
    }

    [Test]
    [Arguments("note-2026-10-24-999999.md")]
    [Arguments("..%2Ffacts%2Fnote.md")]
    public async Task AMissingNoteIsRetried(string name)
    {
        HttpResponseMessage response = await _api.Client().PostAsync($"/api/notes/{name}/retry", null);

        ItShouldNotFindIt();

        void ItShouldNotFindIt() => response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private void Write(string directory, string content) => File.WriteAllText(Path.Combine(directory, NoteName), content);
}