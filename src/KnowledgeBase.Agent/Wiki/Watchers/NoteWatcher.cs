using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Commands;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Watchers;

internal sealed class NoteWatcher(
    ILogger<NoteWatcher> logger,
    IOptions<WikiConfigurationSettings> settings,
    StartNoteProcessingCommandHandler startNoteProcessingCommandHandler)
    : IDisposable
{
    private readonly WikiConfigurationSettings _wikiConfigurationSettings = settings.Value;
    private FileSystemWatcher? _newNotesWatcher;

    public void Dispose()
    {
        _newNotesWatcher?.Created -= OnNewNoteCreated;
        _newNotesWatcher?.Dispose();
    }

    public void Register()
    {
        _newNotesWatcher = new FileSystemWatcher
        {
            Path = _wikiConfigurationSettings.NewNotesDirectory,
            Filter = "*.md",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        _newNotesWatcher.Created += OnNewNoteCreated;
    }

    private async void OnNewNoteCreated(object sender, FileSystemEventArgs e)
    {
        logger.NewNoteCreated(e.FullPath);

        await Result.Try(() => startNoteProcessingCommandHandler
            .Handle(new StartNoteProcessingCommand(e.FullPath), CancellationToken.None)
            .TapError(error => logger.FailedToProcessNewNote(e.FullPath, error)));
    }
}