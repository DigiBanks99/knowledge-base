namespace KnowledgeBase.Agent.Research.Models;

/// <summary>
/// A topic's brief and search queries as the Pleb proposes them, checked before they are stored. The queries leave
/// the machine and the brief reaches the web-facing agents, so privacy is enforced here rather than trusted to the
/// model: neither may contain a denied name. Each error says exactly what to change, so a small model can correct
/// its call.
/// </summary>
public sealed record TopicQueries(string Brief, IReadOnlyList<string> Queries)
{
    public const int MaxQueries = 3;
    public const int MaxQueryLength = 100;
    public const int MaxBriefLength = 500;

    /// <summary>The brief and queries trimmed, or the first thing wrong with them.</summary>
    public static Result<TopicQueries, Error> Validate(string? brief, IReadOnlyList<string>? queries, DenyListMatcher denied)
    {
        string trimmedBrief = brief?.Trim() ?? string.Empty;
        string[] trimmedQueries = [.. (queries ?? []).Select(query => query?.Trim() ?? string.Empty)];

        if (trimmedBrief.Length == 0)
        {
            return Invalid("EmptyBrief", "The `brief` is empty. Describe the topic in one to three sentences.");
        }

        if (trimmedBrief.Length > MaxBriefLength)
        {
            return Invalid(
                "BriefTooLong",
                $"The `brief` is {trimmedBrief.Length} characters; at most {MaxBriefLength} are allowed. Shorten it to one to three sentences.");
        }

        if (trimmedQueries.Length is 0 or > MaxQueries)
        {
            return Invalid(
                "WrongNumberOfQueries",
                $"You gave {trimmedQueries.Length} queries; give between 1 and {MaxQueries}. Keep the most useful ones.");
        }

        for (int index = 0; index < trimmedQueries.Length; index++)
        {
            string query = trimmedQueries[index];
            if (query.Length == 0)
            {
                return Invalid("EmptyQuery", $"Query {index + 1} is empty. Remove it or write a search query.");
            }

            if (query.Length > MaxQueryLength)
            {
                return Invalid(
                    "QueryTooLong",
                    $"Query {index + 1} ('{query}') is {query.Length} characters; at most {MaxQueryLength} are allowed. Use fewer, more specific words.");
            }

            if (trimmedQueries[..index].Contains(query, StringComparer.OrdinalIgnoreCase))
            {
                return Invalid("DuplicateQuery", $"Query {index + 1} ('{query}') repeats an earlier query. Remove it or search for something else.");
            }

            Maybe<string> deniedInQuery = denied.FindDeniedName(query);
            if (deniedInQuery.HasValue)
            {
                return Invalid(
                    "DeniedNameInQuery",
                    $"Query {index + 1} ('{query}') contains the name '{deniedInQuery.Value}'. Names of people and organisations from the wiki must never appear in a query. Rewrite it about the subject without the name.");
            }
        }

        Maybe<string> deniedInBrief = denied.FindDeniedName(trimmedBrief);
        return deniedInBrief.HasValue
            ? Invalid(
                "DeniedNameInBrief",
                $"The `brief` contains the name '{deniedInBrief.Value}'. Names of people and organisations from the wiki must never appear in the brief. Rewrite it without the name.")
            : new TopicQueries(trimmedBrief, trimmedQueries);
    }

    private static Result<TopicQueries, Error> Invalid(string code, string message) => new ValidationError(code, message);
}
