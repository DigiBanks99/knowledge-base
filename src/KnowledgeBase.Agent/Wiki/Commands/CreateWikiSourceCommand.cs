using KnowledgeBase.Agent.Wiki.Files;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki.Commands;

public record CreateWikiSourceCommand(string Name, string Content);

internal sealed class CreateWikiSourceCommandHandler(
    CreateFileHandler fileHandler,
    IOptions<WikiConfigurationSettings> options)
{
    private readonly WikiConfigurationSettings _settings = options.Value;

    public async Task<Result<string, Error>> Handle(
        CreateWikiSourceCommand command,
        CancellationToken cancellationToken)
    {
        string filePath = Path.Combine(_settings.RootDirectory, command.Name);
        CreateFileRequest createFileRequest = new(filePath, command.Content, "text/markdown");
        return await fileHandler.Handle(createFileRequest, cancellationToken);
    }
}