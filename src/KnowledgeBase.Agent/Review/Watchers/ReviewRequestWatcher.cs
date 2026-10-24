using KnowledgeBase.Agent.Common;
using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Review.Commands;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Review.Watchers;

internal sealed class ReviewRequestWatcher(
    ILogger<ReviewRequestWatcher> logger,
    IOptions<WikiConfigurationSettings> settings,
    ReadFileHandler readFileHandler,
    MoveFileHandler moveFileHandler,
    ReviewWikiCommandHandler reviewWikiCommandHandler)
    : IDisposable
{
    private readonly WikiConfigurationSettings _wikiConfigurationSettings = settings.Value;
    private DirectoryWatcher? _newReviewRequestWatcher;

    public void Dispose() => _newReviewRequestWatcher?.Dispose();

    public void Register()
    {
        _newReviewRequestWatcher = new DirectoryWatcher(
            logger,
            _wikiConfigurationSettings.ReviewRequestDirectory,
            "*.md",
            OnNewReviewRequest);
        _newReviewRequestWatcher.Start();
    }

    private async Task OnNewReviewRequest(string requestPath)
    {
        logger.NewReviewRequestRead(requestPath);

        string completedPath = Path.Combine(
            _wikiConfigurationSettings.CompletedReviewRequestDirectory,
            Path.GetFileName(requestPath));

        await readFileHandler
            .Handle(new ReadFileRequest(requestPath), CancellationToken.None)
            .Map(content => DateTimeOffset.Parse(content["Review - ".Length..]))
            .Bind(date => reviewWikiCommandHandler.Handle(
                new ReviewWikiCommand(Path.GetFileName(requestPath), date),
                CancellationToken.None))
            .Bind(_ => moveFileHandler.Handle(new MoveFileRequest(requestPath, completedPath)))
            .TapError(error => logger.FailedToReviewWiki(requestPath, error));
    }
}