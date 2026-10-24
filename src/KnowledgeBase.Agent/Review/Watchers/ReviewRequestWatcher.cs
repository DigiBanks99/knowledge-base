using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Review.Commands;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Review.Watchers;

internal sealed class ReviewRequestWatcher(
    ILogger<ReviewRequestWatcher> logger,
    IOptions<WikiConfigurationSettings> settings,
    ReadFileHandler readFileHandler,
    ReviewWikiCommandHandler reviewWikiCommandHandler)
    : IDisposable
{
    private readonly WikiConfigurationSettings _wikiConfigurationSettings = settings.Value;
    private FileSystemWatcher? _newReviewRequestWatcher;

    public void Dispose()
    {
        _newReviewRequestWatcher?.Created -= OnNewReviewRequestCreated;
        _newReviewRequestWatcher?.Dispose();
    }

    public void Register()
    {
        _newReviewRequestWatcher = new FileSystemWatcher
        {
            Path = _wikiConfigurationSettings.ReviewRequestDirectory,
            Filter = "*.md",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };
        _newReviewRequestWatcher.Created += OnNewReviewRequestCreated;
    }

    private async void OnNewReviewRequestCreated(object sender, FileSystemEventArgs e)
    {
        logger.NewReviewRequestRead(e.FullPath);

        await Result.Try(() => readFileHandler
            .Handle(new ReadFileRequest(e.FullPath), CancellationToken.None)
            .Map(content => DateTimeOffset.Parse(content["Review - ".Length..]))
            .Bind(date => reviewWikiCommandHandler.Handle(new ReviewWikiCommand(date), CancellationToken.None))
            .TapError(error => logger.FailedToReviewWiki(e.FullPath, error)));
    }
}