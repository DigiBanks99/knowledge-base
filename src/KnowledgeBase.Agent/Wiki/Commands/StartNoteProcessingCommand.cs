using KnowledgeBase.Agent.Wiki.Files;
using KnowledgeBase.Agent.Wiki.Queries;
using KnowledgeBase.Agent.Wiki.Tools;

using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki.Commands;

public record StartNoteProcessingCommand(string NotePath);

internal sealed class StartNoteProcessingCommandHandler(
    IOptions<WikiConfigurationSettings> settings,
    [FromKeyedServices(KnownAgents.FactFinder)] AIAgent factFinderClient,
    MoveFileHandler moveFileHandler,
    ReadFileHandler readFileHandler,
    ListWikiNotesQueryHandler listNotesHandler)
{
    private readonly WikiConfigurationSettings _wikiConfigurationSettings = settings.Value;

    public async Task<Result<string, Error>> Handle(
        StartNoteProcessingCommand request,
        CancellationToken cancellationToken) => await
             MoveFileToProcessing(request.NotePath, cancellationToken)
            .Bind(movedPath => readFileHandler.Handle(new ReadFileRequest(movedPath), cancellationToken))
            .Bind(content => FindFactsInNote(content, cancellationToken));

    private async Task<Result<string, Error>> MoveFileToProcessing(
        string notePath,
        CancellationToken cancellationToken)
    {
        await WaitForProcessingSlot(cancellationToken);

        string destinationPath = Path.Combine(_wikiConfigurationSettings.ProcessingNotesDirectory, Path.GetFileName(notePath));
        MoveFileRequest moveFileRequest = new(notePath, destinationPath);

        return moveFileHandler.Handle(moveFileRequest);
    }

    private async Task<Result<string, Error>> FindFactsInNote(
        string content,
        CancellationToken cancellationToken)
    {
        string prompt = 
        $"""
        Extract facts from the following content as they relate to the author.
        Use {nameof(FactTools.FactWriterTool)} to record the facts.

        ## Content

        {content}
        """;

        await factFinderClient.RunAsync(prompt,cancellationToken: cancellationToken);

        return "Fact extraction completed successfully.";
    }

    private async Task<Result<bool, Error>> WaitForProcessingSlot(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Result<Queries.Notes, Error> hasProcessingFile = listNotesHandler
                .Handle(new ListWikiNotesQuery());
            if (hasProcessingFile.IsFailure)
            {
                return hasProcessingFile.Error;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return true;
    }
}