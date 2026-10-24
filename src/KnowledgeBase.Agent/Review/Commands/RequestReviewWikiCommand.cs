using System.Net.Mime;

using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Review.Commands;

public record RequestReviewWikiCommand(DateTimeOffset ReviewDate);

internal sealed class RequestReviewWikiCommandHandler(
    CreateFileHandler fileHandler,
    IOptions<WikiConfigurationSettings> options)
{
    private readonly WikiConfigurationSettings _settings = options.Value;

    public async Task<Result<string, Error>> Handle(
        RequestReviewWikiCommand command,
        CancellationToken cancellationToken)
    {
        string fileName = CreateFileHandle(command.ReviewDate);
        CreateFileRequest createFileRequest = new(fileName, $"Review - {command.ReviewDate:O}", MediaTypeNames.Text.Markdown);
        return await fileHandler.Handle(createFileRequest, cancellationToken);
    }

    private string CreateFileHandle(DateTimeOffset reviewDate)
    {
        string fileName = $"review-{reviewDate:yyyy-MM-dd-HH-mm-ss}.md";
        return Path.Combine(_settings.ReviewRequestDirectory, fileName);
    }
}