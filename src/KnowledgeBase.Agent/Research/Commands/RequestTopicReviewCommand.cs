using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>The Pleb's question to the author when it cannot tell what a topic is about.</summary>
/// <param name="Topic">The topic's wiki path, such as <c>projects/foo</c>.</param>
public record RequestTopicReviewCommand(string Topic, string? Question);

internal sealed class RequestTopicReviewCommandHandler(
    ILogger<RequestTopicReviewCommandHandler> logger,
    IResearchStore store,
    TimeProvider timeProvider)
{
    public const int MaxQuestionLength = 500;

    /// <summary>
    /// Searching a subject the Pleb could only guess at fills the pipeline with pages that merely share a word with it,
    /// so the topic is not searched again until the author answers and the Pleb sets new queries. The current brief
    /// and queries stay, for the author to see what was unclear.
    /// </summary>
    public async Task<Result<ResearchTopic, Error>> Handle(
        RequestTopicReviewCommand request,
        CancellationToken cancellationToken)
    {
        string question = request.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            return new ValidationError(
                "EmptyQuestion",
                "The `question` is empty. Say what is unclear about the topic, for example which of several meanings of a name or acronym it is about.");
        }

        if (question.Length > MaxQuestionLength)
        {
            return new ValidationError(
                "QuestionTooLong",
                $"The `question` is {question.Length} characters; at most {MaxQuestionLength} are allowed. Ask it in one or two sentences.");
        }

        return await store
            .UpdateTopicAsync(
                request.Topic,
                current => !current.HasValue ? new TopicNotFoundError(request.Topic)
                    : current.Value.Pinned ? new TopicPinnedError(request.Topic)
                    : Maybe.From(current.Value with
                    {
                        ReviewQuestion = question,
                        ReviewRequestedAt = timeProvider.GetUtcNow(),
                        ApprovedAt = null
                    }),
                cancellationToken)
            .Map(updated => updated.Value)
            .Tap(topic => logger.TopicReviewRequested(topic.Path));
    }
}
