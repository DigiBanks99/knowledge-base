using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Notes.Queries;
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

        string prompt =
        $"""
        These facts must be woven into the wiki.

        ```json
        {factsResult.Value}
        ```
        """;

        using (await wikiLock.AcquireAsync(cancellationToken))
        {
            await threadWeaver.RunAsync(prompt, cancellationToken: cancellationToken);
        }

        string processedFactPath = Path.Combine(_settings.ProcessedFactsDirectory, Path.GetFileName(request.FactPath));
        return moveFileHandler
            .Handle(new MoveFileRequest(request.FactPath, processedFactPath))
            .Bind(_ => notesQueryHandler.Handle(new ListWikiNotesQuery()))
            .Map(notes => notes.Processing.FirstOrDefault())
            .Ensure(note => note is not null, _ => new NoNoteProcessingError())
            .Bind(note => noteProcessedHandler.Handle(new RecordWikiNoteProcessedCommand(note!)));
    }
}