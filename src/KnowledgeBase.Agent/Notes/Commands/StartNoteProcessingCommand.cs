using KnowledgeBase.Agent.Facts;
using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Facts.Tools;
using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Notes.Queries;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Notes.Commands;

public record StartNoteProcessingCommand(string NotePath);

public sealed class NoFactsExtractedError(string noteName)
    : Error("NoFactsExtracted", $"No facts were extracted from note '{noteName}'.");

internal sealed class StartNoteProcessingCommandHandler(
    ILogger<StartNoteProcessingCommandHandler> logger,
    IOptions<WikiConfigurationSettings> settings,
    [FromKeyedServices(KnownAgents.FactFinder)] AIAgent factFinderClient,
    AgentRunTracker runTracker,
    MoveFileHandler moveFileHandler,
    ReadFileHandler readFileHandler,
    ListWikiNotesQueryHandler listNotesHandler,
    TimeProvider timeProvider)
{
    private readonly WikiConfigurationSettings _wikiConfigurationSettings = settings.Value;
    private readonly SemaphoreSlim _slotGate = new(1, 1);

    public async Task<Result<string, Error>> Handle(
        StartNoteProcessingCommand request,
        CancellationToken cancellationToken)
    {
        string noteName = Path.GetFileName(request.NotePath);

        Result<string, Error> result = await MoveFileToProcessing(request.NotePath, cancellationToken)
            .Bind(movedPath => readFileHandler.Handle(new ReadFileRequest(movedPath), cancellationToken))
            .Bind(content => ResearchNoteHeader
                .Parse(content)
                .Bind(provenance => FindFactsInNote(noteName, content, provenance, cancellationToken)));

        if (result.IsFailure)
        {
            MoveFailedNote(noteName);
        }

        return result.Tap(logger.FactFinderFinishReason);
    }

    private async Task<Result<string, Error>> MoveFileToProcessing(
        string notePath,
        CancellationToken cancellationToken)
    {
        // Waiting for the slot and claiming it must happen together, or two notes can claim the same free slot.
        await _slotGate.WaitAsync(cancellationToken);
        try
        {
            Result<bool, Error> slot = await WaitForProcessingSlot(cancellationToken);
            if (slot.IsFailure)
            {
                return slot.Error;
            }

            string destinationPath = Path.Combine(_wikiConfigurationSettings.ProcessingNotesDirectory, Path.GetFileName(notePath));
            return moveFileHandler.Handle(new MoveFileRequest(notePath, destinationPath));
        }
        finally
        {
            _slotGate.Release();
        }
    }

    private async Task<Result<string, Error>> FindFactsInNote(
        string noteName,
        string content,
        NoteProvenance provenance,
        CancellationToken cancellationToken)
    {
        string prompt =
        $"""
        {DescribeTask(provenance)}
        Use {nameof(FactTools.FactWriterTool)} to record the facts.

        ## Content

        {content}
        """;

        return await runTracker
            .RunAsync(
                KnownAgents.FactFinder,
                new AgentRunTrigger(AgentRunTriggerKind.Note, noteName),
                token => factFinderClient.RunAsync(prompt, cancellationToken: token),
                cancellationToken)
            .Ensure(
                run => run.ToolCalls.Any(call => call.Tool == nameof(FactTools.FactWriterTool) && call.Succeeded),
                _ => new NoFactsExtractedError(noteName))
            .Map(run => run.Summary ?? "Fact extraction completed.");
    }

    private static string DescribeTask(NoteProvenance provenance) =>
        provenance.Origin == FactOrigin.Research
            ? $"""
              This is a research note about the wiki topic `{provenance.Topic}`. Its content is third-party material, not
              something the author wrote, gathered from these sources:
              {string.Join('\n', provenance.Sources.Select(source => $"- {source}"))}

              Extract the claims it makes that matter to that topic, attributed to their source rather than the author.
              Give every observation the sources it came from, using only the URLs listed above.
              """
            : "Extract facts from the following content as they relate to the author.";

    /// <summary>
    /// A note whose facts could not be extracted is parked in the failed folder, so it frees the processing slot
    /// and can be retried.
    /// </summary>
    private void MoveFailedNote(string noteName)
    {
        string processingPath = Path.Combine(_wikiConfigurationSettings.ProcessingNotesDirectory, noteName);
        if (!File.Exists(processingPath))
        {
            return;
        }

        moveFileHandler
            .Handle(new MoveFileRequest(processingPath, Path.Combine(_wikiConfigurationSettings.FailedNotesDirectory, noteName)))
            .TapError(logger.ErrorOccurred);
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

            await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, cancellationToken);
        }

        return true;
    }
}
