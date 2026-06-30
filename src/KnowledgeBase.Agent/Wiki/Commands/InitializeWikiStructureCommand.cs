using KnowledgeBase.Agent.Wiki.Files;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki.Commands;

public record struct InitializeWikiStructureCommand();

internal sealed class InitializeWikiStructureCommandHandler(
    ILogger<InitializeWikiStructureCommandHandler> logger,
    CreateDirectoryHandler directoryHandler,
    IOptions<WikiConfigurationSettings> settingsOptions)
{
    private readonly WikiConfigurationSettings settings = settingsOptions.Value;

    public Result<bool, Error> Handle(InitializeWikiStructureCommand _)
    {
        logger.InitializingWikiStructure();

        CreateDirectoryRequest[] directories =
        [
            CreateDirectoryRequest.Create(settings.NotesDirectory),
            CreateDirectoryRequest.Create(settings.NewNotesDirectory),
            CreateDirectoryRequest.Create(settings.ProcessingNotesDirectory),
            CreateDirectoryRequest.Create(settings.ProcessedNotesDirectory),
            CreateDirectoryRequest.Create(settings.FactsDirectory),
            CreateDirectoryRequest.Create(settings.ThreadsDirectory),
            CreateDirectoryRequest.Create(settings.LogsDirectory),
            CreateDirectoryRequest.Create(settings.WikiDirectory)
        ];

        return Result
            .Combine(directories.Select(directoryHandler.Handle))
            .TapError(logger.FailedToInitializeWikiStructure);
    }
}