using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Search;

/// <summary>A web search the research cycle sends a topic's queries to.</summary>
internal interface ISearchSource
{
    /// <summary>The source's name, as results and the Status page show it.</summary>
    string Name { get; }

    /// <summary>Whether the source has what it needs to be searched; one that is not is skipped.</summary>
    bool IsConfigured { get; }

    /// <summary>The source's results for <paramref name="query"/>, newest first where the source can sort by date.</summary>
    /// <param name="since">The oldest results worth returning, for a source that can filter by date.</param>
    Task<Result<SearchAnswer, Error>> SearchAsync(string query, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Whether the source answers right now, for its status signal.</summary>
    Task<UnitResult<Error>> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A source's answer to one query: its results, and the engines behind it that did not answer, for a source that
/// gathers from several (SearXNG). An answer with gaps still counts as searched; the gaps are recorded on the run.
/// </summary>
internal sealed record SearchAnswer(IReadOnlyList<SearchResult> Results, IReadOnlyList<EngineFailure> Unanswered)
{
    public static SearchAnswer Of(IReadOnlyList<SearchResult> results) => new(results, []);
}

/// <summary>An engine behind a search source that did not answer, and the reason it gave.</summary>
internal sealed record EngineFailure(string Engine, string Reason)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Reason) ? Engine : $"{Engine} ({Reason})";
}

/// <summary>
/// A search source answers, but some of the engines behind it are missing, disabled or failing. Its message names
/// them, as the status signal shows it.
/// </summary>
internal sealed class SearchEnginesDegradedError(string problems) : Error("SearchEnginesDegraded", problems);

/// <summary>A search source could not be reached or answered with something other than results.</summary>
internal sealed class SearchSourceError(string source, string reason)
    : Error("SearchSourceFailed", $"{source} could not be searched: {reason}")
{
    public string Source { get; } = source;
}
