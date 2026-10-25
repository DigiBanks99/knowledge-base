namespace KnowledgeBase.Agent.Review.Models;

public record NewReviewFinding(string PagePath, string Finding, string Recommendation, PriorityLevel Priority);

public record ReviewFinding(
    string Id,
    DateTimeOffset ReviewDate,
    string PagePath,
    string Finding,
    string Recommendation,
    PriorityLevel Priority,
    string? Resolution = null,
    DateTimeOffset? ResolvedAt = null);

public record FindingResolution(string Id, string Resolution);

public record ReviewWikiResult(DateTimeOffset ReviewDate, string Summary, IReadOnlyCollection<string> FindingIds);
