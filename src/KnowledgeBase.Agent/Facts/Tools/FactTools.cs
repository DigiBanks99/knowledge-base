using System.ComponentModel;
using System.Net.Mime;
using System.Text.Json;

using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Notes;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Facts.Tools;

internal sealed class FactTools(
    ILogger<FactTools> logger,
    IOptions<WikiConfigurationSettings> settings,
    AgentRunTracker runTracker,
    CreateFileHandler createFileHandler,
    ReadFileHandler readFileHandler,
    RecordFactSubjectsCommandHandler recordSubjectsHandler)
{
    private readonly WikiConfigurationSettings _settings = settings.Value;

    // The facts are a top-level array rather than a wrapper object: models given `facts: { facts: [...] }` tend to
    // drop or flatten one of the levels.
    [Description("""
                 Writes facts extracted from notes being processed to a fact store for further processing. Use this to persist the facts you've extracted from a note.
                 Pass both `facts` and `collectionName` in one call. Give every subject its `kind`. For a research note, give every observation the `sources` it came from, using only the URLs listed in the note's header.
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
        NoteProvenance provenance = await ReadProvenanceAsync(sourceNote, cancellationToken);
        EnsureEverySubjectHasAKind(facts);
        facts = provenance.Origin == FactOrigin.Research
            ? EnsureSourcesAreFromTheNote(facts, provenance)
            : WithoutSources(facts);
        await DenyPrivateSubjectsAsync(facts, provenance.Origin, cancellationToken);

        logger.SerializingFactsToFile(factFilePath);
        CreateFileRequest createFileCommand = new(
            factFilePath,
            JsonSerializer.Serialize(
                new FactDocument(sourceNote, facts, provenance.Origin, provenance.Topic),
                JsonSerializerOptions.Web),
            MediaTypeNames.Application.Json);

        Result<string, Error> result = await createFileHandler
            .Handle(createFileCommand, cancellationToken)
            .TapError(logger.ErrorOccurred);
        return result.IsFailure
            ? throw new InvalidOperationException($"Failed to write facts to {factFilePath}. Error: {result.Error}")
            : $"Wrote {facts.Length} facts to {collectionName}.";
    }

    /// <summary>
    /// The origin is read from the note's header rather than taken from the model, so research content can never be
    /// recorded as the author's own.
    /// </summary>
    private async Task<NoteProvenance> ReadProvenanceAsync(string? sourceNote, CancellationToken cancellationToken)
    {
        if (sourceNote is null)
        {
            return NoteProvenance.Author;
        }

        string notePath = Path.Combine(_settings.ProcessingNotesDirectory, Path.GetFileName(sourceNote));
        Result<NoteProvenance, Error> provenance = await readFileHandler
            .Handle(new ReadFileRequest(notePath), cancellationToken)
            .Bind(ResearchNoteHeader.Parse)
            .TapError(logger.ErrorOccurred);
        return provenance.IsFailure
            ? throw new InvalidOperationException($"Failed to read the provenance of note '{sourceNote}'. Error: {provenance.Error}")
            : provenance.Value;
    }

    /// <summary>
    /// Done before the facts are written, so no fact document reaches the rest of the pipeline while the people and
    /// organisations it names are still allowed in search queries.
    /// </summary>
    private async Task DenyPrivateSubjectsAsync(Fact[] facts, FactOrigin origin, CancellationToken cancellationToken)
    {
        Result<int, Error> result = await recordSubjectsHandler
            .Handle(new RecordFactSubjectsCommand(facts, origin), cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to add the people and organisations to the deny-list. Error: {result.Error}");
        }
    }

    private static void EnsureEverySubjectHasAKind(Fact[] facts)
    {
        FactSubject? unclassified = facts
            .SelectMany(fact => fact.Observations.SelectMany(observation => observation.RelatedSubjects).Prepend(fact.Subject))
            .FirstOrDefault(subject => subject.Kind is null);
        if (unclassified is not null)
        {
            throw new InvalidOperationException(
                $"The subject '{unclassified.Name}' has no `kind`. Give every subject, including related subjects, a `kind` of `{SubjectKind.Person}`, `{SubjectKind.Organisation}` or `{SubjectKind.Other}`.");
        }
    }

    /// <summary>Only research observations carry sources; the author's own notes are their own source.</summary>
    private static Fact[] WithoutSources(Fact[] facts) =>
    [
        .. facts.Select(fact => fact with
        {
            Observations = [.. fact.Observations.Select(observation => observation with { Sources = null })]
        })
    ];

    private static Fact[] EnsureSourcesAreFromTheNote(Fact[] facts, NoteProvenance provenance)
    {
        foreach (Observation observation in facts.SelectMany(fact => fact.Observations))
        {
            if (observation.Sources is not { Length: > 0 })
            {
                throw new InvalidOperationException(
                    $"The observation '{observation.Summary}' has no `sources`. This is a research note: give every observation the URLs it came from, chosen from: {string.Join(", ", provenance.Sources)}.");
            }

            string? unknown = observation.Sources.FirstOrDefault(source => !provenance.Sources.Contains(source));
            if (unknown is not null)
            {
                throw new InvalidOperationException(
                    $"The source '{unknown}' of observation '{observation.Summary}' is not in the note's header. Use only these URLs, exactly as written: {string.Join(", ", provenance.Sources)}.");
            }
        }

        return facts;
    }
}