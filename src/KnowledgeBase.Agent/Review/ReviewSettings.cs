namespace KnowledgeBase.Agent.Review;

public sealed class ReviewSettings
{
    public const string SectionName = "Review";

    public TimeSpan Interval { get; init; } = TimeSpan.FromDays(1);
}