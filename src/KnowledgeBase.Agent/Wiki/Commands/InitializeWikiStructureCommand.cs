using System.Net.Mime;

using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Wiki.Versioning;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki.Commands;

public record struct InitializeWikiStructureCommand();

internal sealed class InitializeWikiStructureCommandHandler(
    ILogger<InitializeWikiStructureCommandHandler> logger,
    CreateDirectoryHandler directoryHandler,
    CreateFileHandler createFileHandler,
    InitializeWikiRepositoryCommandHandler repositoryHandler,
    PushWikiCommandHandler pushHandler,
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
            .Tap(_ => InitializeRepository(cancellationToken))
            .TapError(logger.FailedToInitializeWikiStructure);
    }

    /// <summary>
    /// Without git the wiki still works, just unversioned, so a repository that cannot be created is logged rather
    /// than stopping the app. Once it exists, any commits the backup remote is missing (such as a new wiki's initial
    /// commit, or ones a failed push left behind before a restart) are pushed rather than waiting for the next agent
    /// run; a failed push is only logged.
    /// </summary>
    private async Task InitializeRepository(CancellationToken cancellationToken) => await repositoryHandler
        .Handle(new InitializeWikiRepositoryCommand(), cancellationToken)
        .TapError(logger.FailedToInitializeWikiRepository)
        .Tap(() => pushHandler.Handle(new PushWikiCommand(OnlyWhenBehind: true), cancellationToken));

    private Result<bool, Error> CreateDirectories()
    {
        CreateDirectoryRequest[] directories =
        [
            CreateDirectoryRequest.Create(settings.NotesDirectory),
            CreateDirectoryRequest.Create(settings.NewNotesDirectory),
            CreateDirectoryRequest.Create(settings.ProcessingNotesDirectory),
            CreateDirectoryRequest.Create(settings.ProcessedNotesDirectory),
            CreateDirectoryRequest.Create(settings.FailedNotesDirectory),
            CreateDirectoryRequest.Create(settings.FactsDirectory),
            CreateDirectoryRequest.Create(settings.ProcessedFactsDirectory),
            CreateDirectoryRequest.Create(settings.FailedFactsDirectory),
            CreateDirectoryRequest.Create(settings.RunsDirectory),
            CreateDirectoryRequest.Create(settings.ConversationsDirectory),
            CreateDirectoryRequest.Create(settings.ThreadsDirectory),
            CreateDirectoryRequest.Create(settings.LogsDirectory),
            CreateDirectoryRequest.Create(settings.ReviewDirectory),
            CreateDirectoryRequest.Create(settings.ReviewRequestDirectory),
            CreateDirectoryRequest.Create(settings.CompletedReviewRequestDirectory),
            CreateDirectoryRequest.Create(settings.ReviewReportsDirectory),
            CreateDirectoryRequest.Create(settings.ReviewFindingsDirectory),
            CreateDirectoryRequest.Create(settings.ReviewOpenFindingsDirectory),
            CreateDirectoryRequest.Create(settings.ReviewClosedFindingsDirectory),
            CreateDirectoryRequest.Create(settings.WikiDirectory),
            CreateDirectoryRequest.Create(settings.ResearchDirectory)
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