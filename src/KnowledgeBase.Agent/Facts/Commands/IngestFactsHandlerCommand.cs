using System.Text.Json;

using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Notes.Queries;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Commands;
using KnowledgeBase.Agent.Wiki.Errors;

using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Facts.Commands;

public record IngestFactsCommand(string FactPath);

internal sealed class IngestFactsCommandHandler(
    ILogger<IngestFactsCommandHandler> logger,
    [FromKeyedServices(KnownAgents.ThreadWeaver)] AIAgent threadWeaver,
    WikiLock wikiLock,
    AgentRunTracker runTracker,
    IOptions<WikiConfigurationSettings> settings,
    MoveFileHandler moveFileHandler,
    ReadFileHandler readFileHandler,
    ListWikiNotesQueryHandler notesQueryHandler,
    RecordWikiNoteProcessedCommandHandler noteProcessedHandler)
{
    private readonly WikiConfigurationSettings _settings = settings.Value;

    public async Task<Result<string, Error>> Handle(
        IngestFactsCommand request,
        CancellationToken cancellationToken)
    {
        Result<string, Error> factsResult = await readFileHandler
            .Handle(new ReadFileRequest(request.FactPath), cancellationToken);
        if (factsResult.IsFailure)
        {
            logger.FailedToReadFile(request.FactPath, factsResult.Error);
            return factsResult.Error;
        }

        string factFileName = Path.GetFileName(request.FactPath);
        FactDocument? document = ReadDocument(factsResult.Value);
        string? notePath = FindSourceNote(document);
        FactOrigin origin = document?.Origin ?? FactOrigin.Research;

        string prompt =
        $"""
        These facts must be woven into the wiki.

        ```json
        {factsResult.Value}
        ```
        """;

        Result<AgentRun, Error> run;
        using (await wikiLock.AcquireAsync(cancellationToken))
        {
            run = await runTracker.RunAsync(
                KnownAgents.ThreadWeaver,
                new AgentRunTrigger(AgentRunTriggerKind.Facts, factFileName),
                origin,
                document?.Topic,
                token => threadWeaver.RunAsync(prompt, cancellationToken: token),
                cancellationToken);
        }

        if (run.IsFailure)
        {
            await MoveFailed(request.FactPath, _settings.FailedFactsDirectory);
            if (notePath is not null)
            {
                await MoveFailed(notePath, _settings.FailedNotesDirectory);
            }

            return run.Error;
        }

        return await moveFileHandler
            .Handle(new MoveFileRequest(request.FactPath, Path.Combine(_settings.ProcessedFactsDirectory, factFileName)), cancellationToken)
            .Bind(_ => notePath is not null
                ? Result.Success<string, Error>(notePath)
                : Result.Failure<string, Error>(new NoNoteProcessingError()))
            .Bind(note => noteProcessedHandler.Handle(new RecordWikiNoteProcessedCommand(note), cancellationToken));
    }

    /// <summary>
    /// The fact document, or <c>null</c> for a file that is not one. A file whose origin cannot be read is woven as
    /// research, so content of unknown provenance never runs with the author's freedom.
    /// </summary>
    private static FactDocument? ReadDocument(string factsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<FactDocument>(factsJson, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The note the facts came from, while it is still processing. Fact files from before notes were recorded on
    /// them fall back to the note currently processing, as only one note is processed at a time.
    /// </summary>
    private string? FindSourceNote(FactDocument? document)
    {
        string? sourceNote = document?.SourceNote;
        if (sourceNote is not null)
        {
            string sourceNotePath = Path.Combine(_settings.ProcessingNotesDirectory, Path.GetFileName(sourceNote));
            return File.Exists(sourceNotePath) ? sourceNotePath : null;
        }

        return notesQueryHandler
            .Handle(new ListWikiNotesQuery())
            .Map(notes => notes.Processing.FirstOrDefault())
            .GetValueOrDefault();
    }

    /// <remarks>Not cancellable: a run that was cancelled must still give up its files.</remarks>
    private Task MoveFailed(string filePath, string failedDirectory) => moveFileHandler
        .Handle(new MoveFileRequest(filePath, Path.Combine(failedDirectory, Path.GetFileName(filePath))), CancellationToken.None)
        .TapError(logger.ErrorOccurred);
}
