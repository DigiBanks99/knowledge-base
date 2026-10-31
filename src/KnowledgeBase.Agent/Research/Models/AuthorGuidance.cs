using System.Text.Json.Serialization;

namespace KnowledgeBase.Agent.Research.Models;

[JsonConverter(typeof(JsonStringEnumConverter<GuidanceKind>))]
public enum GuidanceKind
{
    /// <summary>The author's answer to a question the Pleb asked.</summary>
    Clarification,

    /// <summary>The author's rejection of a curation, with an optional reason.</summary>
    Rejection
}

/// <summary>
/// Something the author told the Pleb about a topic. It is kept on the topic and goes into every later curation
/// prompt, so a recuration that page changes trigger does not lose it.
/// </summary>
/// <param name="Text">The author's answer or reason; a rejection may have none.</param>
/// <param name="Question">For a clarification, the question it answers.</param>
/// <param name="RejectedBrief">For a rejection, the brief that was rejected.</param>
/// <param name="RejectedQueries">For a rejection, the queries that were rejected.</param>
public sealed record AuthorGuidance(
    GuidanceKind Kind,
    string? Text,
    DateTimeOffset At,
    string? Question = null,
    string? RejectedBrief = null,
    IReadOnlyList<string>? RejectedQueries = null)
{
    /// <summary>The longest answer or reason the author may give, so guidance cannot crowd the topic out of the prompt.</summary>
    public const int MaxTextLength = 1000;
}
