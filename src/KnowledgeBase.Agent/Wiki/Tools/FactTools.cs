using System.ComponentModel;
using System.Text.Json;

using KnowledgeBase.Agent.Anthropic;
using KnowledgeBase.Agent.Wiki.Files;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

using static System.Net.Mime.MediaTypeNames;

namespace KnowledgeBase.Agent.Wiki.Tools;

public record FactSubject(string Name, string? Description);
public record Observation(string Summary, string Detail, int Priority, FactSubject[] RelatedSubjects);

public record Fact(
    FactSubject Subject,
    Observation[] Observations);

public record FactContainer(Fact[] Facts);

internal sealed class FactTools(
    ILogger<FactTools> logger,
    IOptions<WikiConfigurationSettings> settings,
    CreateFileHandler createFileHandler)
{
    private readonly WikiConfigurationSettings _settings = settings.Value;

    [Description("Writes facts extracted from notes being processed to a fact store for further processing. Use this to persist the facts you've extracted from a note.")]
    public async Task<FactContainer> FactWriterTool(
        [Description("The container of facts that were extracted and need to be persisted.")] FactContainer facts,
        [Description("The fact collection name. This will be used to identify the collection of facts in the fact store.")] string collectionName,
        CancellationToken cancellationToken)
    {
        string factFilePath = Path.Combine(_settings.FactsDirectory, $"{collectionName}.json");

        logger.SerializingFactsToFile(factFilePath);
        CreateFileRequest createFileCommand = new(
            factFilePath,
            JsonSerializer.Serialize(facts, JsonSerializerOptions.Web),
            Application.Json);

        Result<string, Error> result = await createFileHandler
            .Handle(createFileCommand, cancellationToken);
        return result.IsFailure
            ? throw new InvalidOperationException($"Failed to write facts to {factFilePath}. Error: {result.Error}")
            : facts;
    }
}

internal static class FactToolsExtensions
{
    extension(AIAgentToolsBuilder builder)
    {
        public AIAgentToolsBuilder WithFactTools()
        {
            AgentToolCollection tools = new();
            foreach (AIFunction tool in builder.Tools)
            {
                tools.AddTool(tool);
            }
            builder.HostBuilder.Services.AddKeyedSingleton(builder.AgentName, tools);
            return builder;
        }
    }
}