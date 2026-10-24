using KnowledgeBase.Agent.Common;
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
    private DirectoryWatcher? _newFactsWatcher;

    public void Dispose() => _newFactsWatcher?.Dispose();

    public void Register()
    {
        _newFactsWatcher = new DirectoryWatcher(logger, _wikiConfigurationSettings.FactsDirectory, "*.json", OnNewFact);
        _newFactsWatcher.Start();
    }

    private async Task OnNewFact(string factPath)
    {
        logger.NewFactCreated(factPath);

        await ingestFactsHandler
            .Handle(new IngestFactsCommand(factPath), CancellationToken.None)
            .TapError(error => logger.FailedToIngestNewFact(factPath, error));
    }
}
