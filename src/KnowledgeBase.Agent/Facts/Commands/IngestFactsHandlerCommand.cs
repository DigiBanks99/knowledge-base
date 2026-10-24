using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Notes.Queries;
using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Commands;
using KnowledgeBase.Agent.Wiki.Errors;

using Microsoft.Agents.AI;

namespace KnowledgeBase.Agent.Facts.Commands;

public record IngestFactsCommand(string FactPath);

internal sealed class IngestFactsCommandHandler(
    ILogger<IngestFactsCommandHandler> logger,
    [FromKeyedServices(KnownAgents.ThreadWeaver)] AIAgent threadWeaver,
    WikiLock wikiLock,
    ReadFileHandler readFileHandler,
    ListWikiNotesQueryHandler notesQueryHandler,
    RecordWikiNoteProcessedCommandHandler noteProcessedHandler)
{
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

        return notesQueryHandler
            .Handle(new ListWikiNotesQuery())
            .Map(notes => notes.Processing.FirstOrDefault())
            .Ensure(note => note is not null, _ => new NoNoteProcessingError())
            .Bind(note => noteProcessedHandler.Handle(new RecordWikiNoteProcessedCommand(note!)));
    }
}