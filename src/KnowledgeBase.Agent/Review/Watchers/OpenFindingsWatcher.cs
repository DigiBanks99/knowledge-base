using KnowledgeBase.Agent.Review.Commands;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Review.Watchers;

/// <summary>
/// Starts the Editor when new findings are opened. A review writes many finding files at once, so triggers are
/// debounced and coalesced: only one Editor run happens at a time, and findings opened during a run cause one
/// more run once it completes.
/// </summary>
internal sealed class OpenFindingsWatcher(
    ILogger<OpenFindingsWatcher> logger,
    IOptions<WikiConfigurationSettings> settings,
    EditWikiCommandHandler editWikiCommandHandler)
    : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(5);

    private readonly WikiConfigurationSettings _wikiConfigurationSettings = settings.Value;
    private readonly SemaphoreSlim _editorGate = new(1, 1);
    private FileSystemWatcher? _openFindingsWatcher;
    private int _pending;

    public void Dispose()
    {
        _openFindingsWatcher?.Created -= OnFindingOpened;
        _openFindingsWatcher?.Dispose();
        _editorGate.Dispose();
    }

    public void Register()
    {
        _openFindingsWatcher = new FileSystemWatcher
        {
            Path = _wikiConfigurationSettings.ReviewOpenFindingsDirectory,
            Filter = "*.json",
            NotifyFilter = NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _openFindingsWatcher.Created += OnFindingOpened;
    }

    private async void OnFindingOpened(object sender, FileSystemEventArgs e)
    {
        logger.NewFindingOpened(e.FullPath);
        Interlocked.Exchange(ref _pending, 1);

        while (Volatile.Read(ref _pending) == 1 && await _editorGate.WaitAsync(0))
        {
            try
            {
                await Task.Delay(DebounceDelay);
                while (Interlocked.Exchange(ref _pending, 0) == 1)
                {
                    await Result.Try(() => editWikiCommandHandler
                        .Handle(new EditWikiCommand(), CancellationToken.None)
                        .Tap(logger.EditorFinished)
                        .TapError(logger.FailedToEditWiki));
                }
            }
            finally
            {
                _editorGate.Release();
            }
        }
    }
}
