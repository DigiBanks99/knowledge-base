using System.Net.Mime;

using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Notes.Commands;

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
        CreateFileRequest createFileRequest = new(filePath, command.Content, MediaTypeNames.Text.Markdown);
        return await fileHandler.Handle(createFileRequest, cancellationToken);
    }
}
