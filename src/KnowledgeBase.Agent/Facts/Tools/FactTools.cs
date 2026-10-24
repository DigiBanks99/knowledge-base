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

    // The facts are a top-level array rather than a wrapper object: models given `facts: { facts: [...] }` tend to
    // drop or flatten one of the levels.
    [Description("""
                 Writes facts extracted from notes being processed to a fact store for further processing. Use this to persist the facts you've extracted from a note.
                 Pass both `facts` and `collectionName` in one call.
                 """)]
    public async Task<string> FactWriterTool(
        [Description("Every fact extracted from the note: one entry per subject, each with its observations.")] Fact[] facts,
        [Description("""
                     The fact collection name, used to identify the collection in the fact store. Descriptive and kebab-case, without an extension, for example `thoughts-on-the-relationship-with-susan-and-growth`.
                     """)] string collectionName,
        CancellationToken cancellationToken)
    {
        string factFilePath = Path.Combine(_settings.FactsDirectory, $"{collectionName}.json");

        AgentRun? run = runTracker.Current;
        string? sourceNote = run?.Trigger.Kind == AgentRunTriggerKind.Note ? run.Trigger.Reference : null;

        logger.SerializingFactsToFile(factFilePath);
        CreateFileRequest createFileCommand = new(
            factFilePath,
            JsonSerializer.Serialize(new FactDocument(sourceNote, facts), JsonSerializerOptions.Web),
            MediaTypeNames.Application.Json);

        Result<string, Error> result = await createFileHandler
            .Handle(createFileCommand, cancellationToken)
            .TapError(logger.ErrorOccurred);
        return result.IsFailure
            ? throw new InvalidOperationException($"Failed to write facts to {factFilePath}. Error: {result.Error}")
            : $"Wrote {facts.Length} facts to {collectionName}.";
    }
}