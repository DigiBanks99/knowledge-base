using KnowledgeBase.Agent.Notes.Queries;

namespace KnowledgeBase.Agent.IntegrationTests.Notes;

public sealed class NoteCatalogTests : IAsyncDisposable
{
    private readonly TestWiki _wiki = new();
    private readonly NoteCatalog _catalog;

    public NoteCatalogTests()
    {
        _catalog = _wiki.Get<NoteCatalog>();
    }

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task NotesAreSpreadAcrossThePipeline()
    {
        await _wiki.InitialiseAsync();
        _wiki.Write(_wiki.Settings.NewNotesDirectory, "note-2026-10-24-090000.md", "New.");
        _wiki.Write(_wiki.Settings.ProcessingNotesDirectory, "note-2026-10-24-080000.md", "Processing.");
        _wiki.Write(_wiki.Settings.ProcessedNotesDirectory, "note-2026-10-23-120000.md", "Processed.");
        _wiki.Write(_wiki.Settings.FailedNotesDirectory, "note-2026-10-24-100000.md", "Failed.");

        IReadOnlyList<NoteSummary> notes = await _catalog.ListAsync(CancellationToken.None);

        ItShouldListTheNewestCaptureFirst();
        ItShouldTakeEachStateFromItsFolder();
        ItShouldReadTheCaptureTimeFromTheName();

        void ItShouldListTheNewestCaptureFirst() => notes.Select(note => note.Name).ShouldBe(
            ["note-2026-10-24-100000.md", "note-2026-10-24-090000.md", "note-2026-10-24-080000.md", "note-2026-10-23-120000.md"]);

        void ItShouldTakeEachStateFromItsFolder() => notes.Select(note => note.State).ShouldBe(
            [NoteState.Failed, NoteState.New, NoteState.Processing, NoteState.Processed]);

        void ItShouldReadTheCaptureTimeFromTheName() =>
            notes[0].CapturedAt.ShouldBe(DateTimeOffset.Parse("2026-10-24T10:00:00Z"));
    }

    [Test]
    public async Task ALongNoteIsListed()
    {
        await _wiki.InitialiseAsync();
        _wiki.Write(_wiki.Settings.NewNotesDirectory, "note-2026-10-24-090000.md", new string('a', 300));

        IReadOnlyList<NoteSummary> notes = await _catalog.ListAsync(CancellationToken.None);

        ItShouldShortenThePreview();

        void ItShouldShortenThePreview() => notes.ShouldHaveSingleItem().Preview.ShouldBe($"{new string('a', 200)}…");
    }

    [Test]
    public async Task ANoteIsLookedUp()
    {
        await _wiki.InitialiseAsync();
        _wiki.Write(_wiki.Settings.FailedNotesDirectory, "note-2026-10-24-090000.md", "A failed note.");

        NoteDetail? note = await _catalog.GetAsync("note-2026-10-24-090000.md", CancellationToken.None);

        ItShouldFindItWhereverItIs();

        void ItShouldFindItWhereverItIs() => note.ShouldNotBeNull().ShouldSatisfyAllConditions(
            detail => detail.Content.ShouldBe("A failed note."),
            detail => detail.Summary.State.ShouldBe(NoteState.Failed));
    }

    [Test]
    [Arguments("note-2026-10-24-090000.md")]
    [Arguments("../../facts/note-2026-10-24-090000.md")]
    public async Task AMissingNoteIsLookedUp(string name)
    {
        await _wiki.InitialiseAsync();
        _wiki.Write(_wiki.Settings.FactsDirectory, "note-2026-10-24-090000.md", "Not a note.");

        NoteDetail? note = await _catalog.GetAsync(name, CancellationToken.None);

        ItShouldFindNothing();

        void ItShouldFindNothing() => note.ShouldBeNull();
    }
}
