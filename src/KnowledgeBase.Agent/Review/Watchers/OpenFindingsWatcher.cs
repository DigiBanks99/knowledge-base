using KnowledgeBase.Agent.Common;
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
    private DirectoryWatcher? _openFindingsWatcher;
    private int _pending;

    public void Dispose()
    {
        _openFindingsWatcher?.Dispose();
        _editorGate.Dispose();
    }

    public void Register()
    {
        _openFindingsWatcher = new DirectoryWatcher(
            logger,
            _wikiConfigurationSettings.ReviewOpenFindingsDirectory,
            "*.json",
            OnFindingOpened);
        _openFindingsWatcher.Start();
    }

    private async Task OnFindingOpened(string findingPath)
    {
        logger.NewFindingOpened(findingPath);
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