using System.Globalization;

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
    ReviewWikiCommandHandler reviewWikiCommandHandler,
    TimeProvider timeProvider)
    : IDisposable
{
    private readonly WikiConfigurationSettings _wikiConfigurationSettings = settings.Value;
    private DirectoryWatcher? _newReviewRequestWatcher;

    public void Dispose() => _newReviewRequestWatcher?.Dispose();

    public void Register()
    {
        _newReviewRequestWatcher = new DirectoryWatcher(
            logger,
            timeProvider,
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
            .Map(content => ReadRequestTime(requestPath, content))
            .Bind(date => reviewWikiCommandHandler.Handle(
                new ReviewWikiCommand(Path.GetFileName(requestPath), date),
                CancellationToken.None))
            .Bind(_ => moveFileHandler.Handle(new MoveFileRequest(requestPath, completedPath), CancellationToken.None))
            .TapError(error => logger.FailedToReviewWiki(requestPath, error));
    }

    /// <summary>
    /// The time the review was requested, as <see cref="RequestReviewWikiCommandHandler"/> writes it. The time only
    /// labels the review, so a request file written some other way, such as by hand, still starts a review, timed by
    /// when the file was written, rather than staying in the requests folder for good.
    /// </summary>
    private DateTimeOffset ReadRequestTime(string requestPath, string content)
    {
        if (content.StartsWith(RequestReviewWikiCommandHandler.ContentPrefix, StringComparison.Ordinal)
            && DateTimeOffset.TryParse(
                content[RequestReviewWikiCommandHandler.ContentPrefix.Length..].Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset requestedAt))
        {
            return requestedAt;
        }

        logger.UnreadableReviewRequestTime(requestPath);
        return new DateTimeOffset(File.GetLastWriteTimeUtc(requestPath), TimeSpan.Zero);
    }
}
