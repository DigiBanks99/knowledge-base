using KnowledgeBase.Agent.Facts.Tools;
using KnowledgeBase.Agent.Facts;
using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Notes.Queries;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Notes.Commands;

public record StartNoteProcessingCommand(string NotePath);

internal sealed class StartNoteProcessingCommandHandler(
    ILogger<StartNoteProcessingCommandHandler> logger,
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
            .Bind(content => FindFactsInNote(content, cancellationToken))
            .Tap(logger.FactFinderFinishReason);

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

        AgentResponse response = await factFinderClient.RunAsync(prompt,cancellationToken: cancellationToken);
        return response.FinishReason.ToString() ?? "Fact extraction completed successfully.";
    }

    private async Task<Result<bool, Error>> WaitForProcessingSlot(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Result<Notes.Models.Notes, Error> hasProcessingFile = listNotesHandler
                .Handle(new ListWikiNotesQuery());
            if (hasProcessingFile.IsFailure)
            {
                return hasProcessingFile.Error;
            }

            if (!hasProcessingFile.Value.Processing.Any())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return true;
    }
}