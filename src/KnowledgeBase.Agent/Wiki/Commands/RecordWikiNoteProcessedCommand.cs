using KnowledgeBase.Agent.Files;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki.Commands;

public record RecordWikiNoteProcessedCommand(string NotePath);

internal sealed class RecordWikiNoteProcessedCommandHandler(
    IOptions<WikiConfigurationSettings> settings,
    MoveFileHandler moveFileHandler)
{
    private readonly WikiConfigurationSettings _settings = settings.Value;

    public Result<string, Error> Handle(RecordWikiNoteProcessedCommand request)
    {
        string destinationPath = Path.Combine(_settings.ProcessedNotesDirectory, Path.GetFileName(request.NotePath));
        MoveFileRequest moveFileRequest = new(request.NotePath, destinationPath);

        return moveFileHandler.Handle(moveFileRequest);
    }
}
