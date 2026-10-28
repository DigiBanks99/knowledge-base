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
    Task<Result<IReadOnlyList<SearchResult>, Error>> SearchAsync(string query, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Whether the source answers right now, for its status signal.</summary>
    Task<UnitResult<Error>> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>A search source could not be reached or answered with something other than results.</summary>
internal sealed class SearchSourceError(string source, string reason)
    : Error("SearchSourceFailed", $"{source} could not be searched: {reason}")
{
    public string Source { get; } = source;
}
