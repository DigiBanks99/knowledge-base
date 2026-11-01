using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;

namespace KnowledgeBase.Agent.IntegrationTests;

/// <summary>
/// A search source standing in for the web, so a search cycle never leaves the machine. It answers every query with
/// <see cref="Results"/>, which a test scripts; by default it finds nothing.
/// </summary>
internal sealed class FakeSearchSource : ISearchSource
{
    public const string SourceName = "Fake search";

    public string Name => SourceName;

    public bool IsConfigured => true;

    /// <summary>The results every query finds.</summary>
    public IReadOnlyList<SearchResult> Results { get; set; } = [];

    public Task<Result<IReadOnlyList<SearchResult>, Error>> SearchAsync(
        string query,
        DateTimeOffset since,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<SearchResult>, Error>(Results));

    public Task<UnitResult<Error>> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(UnitResult.Success<Error>());
}
