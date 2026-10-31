using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>What the author's review actions share: checking what the author typed, and queueing a curation.</summary>
internal static class AuthorCuration
{
    /// <summary>Queues a curation of <paramref name="topic"/> that the author asked for, ahead of those page changes asked for.</summary>
    public static async Task<Result<ResearchTopic, Error>> RequestAsync(
        IResearchStore store,
        ResearchTopic topic,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) => await store
        .RequestCurationAsync(new CurationRequest(topic.Path, [], timeProvider.GetUtcNow(), ByAuthor: true), cancellationToken)
        .Map(_ => topic);
}

/// <summary>An answer or reason the author typed, trimmed and length-checked.</summary>
internal static class AuthorText
{
    /// <summary>The trimmed text (none when it is optional and blank), or what is wrong with it.</summary>
    public static Result<string?, Error> Validate(string? text, string name, bool required)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return required
                ? new ValidationError("EmptyText", $"The {name} is empty.")
                : Result.Success<string?, Error>(null);
        }

        return trimmed.Length > AuthorGuidance.MaxTextLength
            ? new ValidationError("TextTooLong", $"The {name} is {trimmed.Length} characters; at most {AuthorGuidance.MaxTextLength} are allowed.")
            : trimmed;
    }
}
