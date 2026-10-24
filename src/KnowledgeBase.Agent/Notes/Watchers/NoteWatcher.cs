using KnowledgeBase.Agent.Common;
using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Notes.Commands;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Notes.Watchers;

internal sealed class NoteWatcher(
    ILogger<NoteWatcher> logger,
    IOptions<WikiConfigurationSettings> settings,
    MoveFileHandler moveFileHandler,
    StartNoteProcessingCommandHandler startNoteProcessingCommandHandler,
    TimeProvider timeProvider)
    : IDisposable
{
    private readonly WikiConfigurationSettings _wikiConfigurationSettings = settings.Value;
    private DirectoryWatcher? _newNotesWatcher;

    public void Dispose() => _newNotesWatcher?.Dispose();

    public void Register()
    {
        RequeueInterruptedNotes();

        _newNotesWatcher = new DirectoryWatcher(logger, timeProvider, _wikiConfigurationSettings.NewNotesDirectory, "*.md", OnNewNote);
        _newNotesWatcher.Start();
    }

    private async Task OnNewNote(string notePath)
    {
        logger.NewNoteCreated(notePath);

        await startNoteProcessingCommandHandler
            .Handle(new StartNoteProcessingCommand(notePath), CancellationToken.None)
            .TapError(error => logger.FailedToProcessNewNote(notePath, error));
    }

    /// <summary>
    /// A note left in processing with no facts waiting to be woven was interrupted before its facts were written
    /// (for example by a restart), and would block the processing slot forever. Move it back so it is processed again.
    /// Notes whose facts are still waiting are left alone; weaving those facts completes them.
    /// </summary>
    private void RequeueInterruptedNotes()
    {
        bool hasPendingFacts = Directory
            .EnumerateFiles(_wikiConfigurationSettings.FactsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Any();
        if (hasPendingFacts)
        {
            return;
        }

        foreach (string notePath in Directory.EnumerateFiles(_wikiConfigurationSettings.ProcessingNotesDirectory, "*.md"))
        {
            logger.RequeueingInterruptedNote(notePath);
            string newPath = Path.Combine(_wikiConfigurationSettings.NewNotesDirectory, Path.GetFileName(notePath));
            moveFileHandler
                .Handle(new MoveFileRequest(notePath, newPath))
                .TapError(error => logger.FailedToProcessNewNote(notePath, error));
        }
    }
}