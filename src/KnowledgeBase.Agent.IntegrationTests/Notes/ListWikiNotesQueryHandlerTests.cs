using KnowledgeBase.Agent.Notes.Queries;

namespace KnowledgeBase.Agent.IntegrationTests.Notes;

public sealed class ListWikiNotesQueryHandlerTests : IAsyncDisposable
{
    private readonly TestWiki _wiki = new();
    private readonly ListWikiNotesQueryHandler _handler;

    public ListWikiNotesQueryHandlerTests()
    {
        _handler = _wiki.Get<ListWikiNotesQueryHandler>();
    }

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task NotesAreSpreadAcrossThePipeline()
    {
        await _wiki.InitialiseAsync();
        string newNote = _wiki.Write(_wiki.Settings.NewNotesDirectory, "note-2026-10-24-090000.md", "New.");
        string processingNote = _wiki.Write(_wiki.Settings.ProcessingNotesDirectory, "note-2026-10-24-080000.md", "Processing.");
        string processedNote = _wiki.Write(_wiki.Settings.ProcessedNotesDirectory, "note-2026-10-23-120000.md", "Processed.");
        string failedNote = _wiki.Write(_wiki.Settings.FailedNotesDirectory, "note-2026-10-24-100000.md", "Failed.");

        Result<KnowledgeBase.Agent.Notes.Models.Notes, Error> notes = _handler.Handle(new ListWikiNotesQuery());

        ItShouldListEveryFolder();

        void ItShouldListEveryFolder() => notes.Value.ShouldSatisfyAllConditions(
            listed => listed.New.ShouldBe([newNote]),
            listed => listed.Processing.ShouldBe([processingNote]),
            listed => listed.Processed.ShouldBe([processedNote]),
            listed => listed.Failed.ShouldBe([failedNote]));
    }
}
