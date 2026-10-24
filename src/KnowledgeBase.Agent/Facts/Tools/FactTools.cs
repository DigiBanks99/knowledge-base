using System.ComponentModel;
using System.Net.Mime;
using System.Text.Json;

using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Facts.Tools;

internal sealed class FactTools(
    ILogger<FactTools> logger,
    IOptions<WikiConfigurationSettings> settings,
    AgentRunTracker runTracker,
    CreateFileHandler createFileHandler)
{
    private readonly WikiConfigurationSettings _settings = settings.Value;

    [Description("""
                 Writes facts extracted from notes being processed to a fact store for further processing. Use this to persist the facts you've extracted from a note.
                 Give the fact file a descriptive, kebab-case name of the facts plus the json extension/
                 """)]
    public async Task<FactContainer> FactWriterTool(
        [Description("The container of facts that were extracted and need to be persisted.")] FactContainer facts,
        [Description("""
                     The fact collection name. This will be used to identify the collection of facts in the fact store. Kebab-case, for example `thoughts-on-the-relationship-with-susan-and-growth.json`
                     """)] string collectionName,
        CancellationToken cancellationToken)
    {
        string factFilePath = Path.Combine(_settings.FactsDirectory, $"{collectionName}.json");

        AgentRun? run = runTracker.Current;
        string? sourceNote = run?.Trigger.Kind == AgentRunTriggerKind.Note ? run.Trigger.Reference : null;

        logger.SerializingFactsToFile(factFilePath);
        CreateFileRequest createFileCommand = new(
            factFilePath,
            JsonSerializer.Serialize(new FactDocument(sourceNote, facts.Facts), JsonSerializerOptions.Web),
            MediaTypeNames.Application.Json);

        Result<string, Error> result = await createFileHandler
            .Handle(createFileCommand, cancellationToken)
            .TapError(logger.ErrorOccurred);
        return result.IsFailure
            ? throw new InvalidOperationException($"Failed to write facts to {factFilePath}. Error: {result.Error}")
            : facts;
    }
}