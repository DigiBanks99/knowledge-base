using System.Net.Mime;

using KnowledgeBase.Agent.Files;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki.Commands;

public record struct InitializeWikiStructureCommand();

internal sealed class InitializeWikiStructureCommandHandler(
    ILogger<InitializeWikiStructureCommandHandler> logger,
    CreateDirectoryHandler directoryHandler,
    CreateFileHandler createFileHandler,
    IOptions<WikiConfigurationSettings> settingsOptions)
{
    private readonly WikiConfigurationSettings settings = settingsOptions.Value;

    public async Task<Result<bool, Error>> Handle(
        InitializeWikiStructureCommand _,
        CancellationToken cancellationToken)
    {
        logger.InitializingWikiStructure();

        return await CreateDirectories()
            .Bind(_ => SeedIndexPages(cancellationToken))
            .TapError(logger.FailedToInitializeWikiStructure);
    }

    private Result<bool, Error> CreateDirectories()
    {
        CreateDirectoryRequest[] directories =
        [
            CreateDirectoryRequest.Create(settings.NotesDirectory),
            CreateDirectoryRequest.Create(settings.NewNotesDirectory),
            CreateDirectoryRequest.Create(settings.ProcessingNotesDirectory),
            CreateDirectoryRequest.Create(settings.ProcessedNotesDirectory),
            CreateDirectoryRequest.Create(settings.FactsDirectory),
            CreateDirectoryRequest.Create(settings.ThreadsDirectory),
            CreateDirectoryRequest.Create(settings.LogsDirectory),
            CreateDirectoryRequest.Create(settings.ReviewDirectory),
            CreateDirectoryRequest.Create(settings.ReviewRequestDirectory),
            CreateDirectoryRequest.Create(settings.ReviewReportsDirectory),
            CreateDirectoryRequest.Create(settings.ReviewFindingsDirectory),
            CreateDirectoryRequest.Create(settings.ReviewOpenFindingsDirectory),
            CreateDirectoryRequest.Create(settings.ReviewClosedFindingsDirectory),
            CreateDirectoryRequest.Create(settings.WikiDirectory)
        ];

        return Result.Combine(directories.Select(directoryHandler.Handle));
    }

    private async Task<Result<bool, Error>> SeedIndexPages(CancellationToken cancellationToken)
    {
        (string Path, string Content)[] indexPages =
        [
            (Path.Combine(settings.WikiDirectory, "index.md"), WikiSections.RootIndex()),
            .. WikiSections.All.Select(section =>
                (Path.Combine(settings.WikiDirectory, section.Folder, "index.md"), WikiSections.SectionIndex(section)))
        ];

        foreach ((string path, string content) in indexPages.Where(page => !File.Exists(page.Path)))
        {
            Result<string, Error> result = await createFileHandler
                .Handle(new CreateFileRequest(path, content, MediaTypeNames.Text.Markdown), cancellationToken);
            if (result.IsFailure)
            {
                return result.Error;
            }
        }

        return true;
    }
}
