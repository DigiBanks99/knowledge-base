namespace KnowledgeBase.Agent.Research.Models;

/// <summary>
/// A curation that ended without the Pleb setting or keeping the topic's queries, kept in <c>research/curation/failed</c>
/// for the author to inspect. It is cleared when a later curation of the topic succeeds.
/// </summary>
public sealed record FailedCuration(CurationRequest Request, string Reason, DateTimeOffset FailedAt);
