using KnowledgeBase.Agent.Facts.Commands;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Facts.Watchers;

internal sealed class FactWatcher(
    ILogger<FactWatcher> logger,
    IOptions<WikiConfigurationSettings> settings,
    IngestFactsCommandHandler ingestFactsHandler)
    : IDisposable
{
    private readonly WikiConfigurationSettings _wikiConfigurationSettings = settings.Value;
    private FileSystemWatcher? _newFactsWatcher;

    public void Dispose()
    {
        _newFactsWatcher?.Created -= OnNewFactCreated;
        _newFactsWatcher?.Dispose();
    }

    public void Register()
    {
        _newFactsWatcher = new FileSystemWatcher
        {
            Path = _wikiConfigurationSettings.FactsDirectory,
            Filter = "*.json",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        _newFactsWatcher.Created += OnNewFactCreated;
    }

    private async void OnNewFactCreated(object sender, FileSystemEventArgs e)
    {
        logger.NewFactCreated(e.FullPath);

        await Result.Try(() => ingestFactsHandler
            .Handle(new IngestFactsCommand(e.FullPath), CancellationToken.None)
            .TapError(error => logger.FailedToIngestNewFact(e.FullPath, error)));
    }
}