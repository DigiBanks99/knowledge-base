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

        CreateDirectory[] directories =
        [
            CreateDirectory.Create(settings.NotesDirectory),
            CreateDirectory.Create(settings.NewNotesDirectory),
            CreateDirectory.Create(settings.ProcessingNotesDirectory),
            CreateDirectory.Create(settings.ProcessedNotesDirectory),
            CreateDirectory.Create(settings.FactsDirectory),
            CreateDirectory.Create(settings.ThreadsDirectory),
            CreateDirectory.Create(settings.LogsDirectory),
            CreateDirectory.Create(settings.WikiDirectory)
        ];

        return Result
            .Combine(directories.Select(directoryHandler.Handle))
            .TapError(logger.FailedToInitializeWikiStructure);
    }
}