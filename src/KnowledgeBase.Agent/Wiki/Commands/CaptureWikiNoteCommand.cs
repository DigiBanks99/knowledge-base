using KnowledgeBase.Agent.Wiki.Files;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki.Commands;

public record CaptureWikiNoteCommand(string Name, string Content);

internal sealed class CaptureWikiNoteCommandHandler(
    CreateFileHandler fileHandler,
    IOptions<WikiConfigurationSettings> options)
{
    private readonly WikiConfigurationSettings _settings = options.Value;

    public async Task<Result<string, Error>> Handle(
        CaptureWikiNoteCommand command,
        CancellationToken cancellationToken)
    {
        string filePath = Path.Combine(_settings.NewNotesDirectory, command.Name);
        CreateFileRequest createFileRequest = new(filePath, command.Content, "text/markdown");
        return await fileHandler.Handle(createFileRequest, cancellationToken);
    }
}