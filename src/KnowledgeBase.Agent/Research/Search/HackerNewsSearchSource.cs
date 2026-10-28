using System.Text.Json;

using KnowledgeBase.Agent.Research.Models;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Search;

/// <summary>
/// Hacker News stories through the Algolia search API, newest first and no older than asked. A story without a link
/// (an Ask HN post) is its discussion page.
/// </summary>
internal sealed class HackerNewsSearchSource(IHttpClientFactory httpClientFactory, IOptions<ResearchSettings> settings)
    : ISearchSource
{
    public const string HttpClientName = "HackerNews";
    public const string SourceName = "Hacker News";

    private readonly ResearchSettings _settings = settings.Value;

    public string Name => SourceName;

    public bool IsConfigured => true;

    public async Task<Result<IReadOnlyList<SearchResult>, Error>> SearchAsync(
        string query,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        string parameters = string.Join('&',
            $"query={Uri.EscapeDataString(query)}",
            "tags=story",
            $"hitsPerPage={Math.Max(0, _settings.ResultsPerQuery)}",
            $"numericFilters={Uri.EscapeDataString($"created_at_i>{since.ToUnixTimeSeconds()}")}");
        Result<JsonDocument, Error> answer = await SearchHttp.GetJsonAsync(
            httpClientFactory.CreateClient(HttpClientName),
            SearchHttp.Route(_settings.HackerNewsEndpoint, $"search_by_date?{parameters}"),
            Name,
            cancellationToken);
        if (answer.IsFailure)
        {
            return answer.Error;
        }

        using JsonDocument body = answer.Value;
        if (!body.RootElement.TryGetProperty("hits", out JsonElement hits) || hits.ValueKind != JsonValueKind.Array)
        {
            return new SearchSourceError(Name, "the answer has no `hits` array.");
        }

        return Result.Success<IReadOnlyList<SearchResult>, Error>([.. hits.EnumerateArray().Select(ToResult).Choose()]);
    }

    public async Task<UnitResult<Error>> ProbeAsync(CancellationToken cancellationToken)
    {
        Result<JsonDocument, Error> answer = await SearchHttp.GetJsonAsync(
            httpClientFactory.CreateClient(HttpClientName),
            SearchHttp.Route(_settings.HackerNewsEndpoint, "search?tags=story&hitsPerPage=1"),
            Name,
            cancellationToken);
        if (answer.IsFailure)
        {
            return answer.Error;
        }

        answer.Value.Dispose();
        return UnitResult.Success<Error>();
    }

    private Maybe<SearchResult> ToResult(JsonElement hit)
    {
        string? title = SearchHttp.String(hit, "title");
        string? id = SearchHttp.String(hit, "objectID");
        string? url = SearchHttp.String(hit, "url");
        if (string.IsNullOrWhiteSpace(title) || (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(id)))
        {
            return Maybe.None;
        }

        DateTimeOffset? publishedAt = hit.TryGetProperty("created_at_i", out JsonElement created) && created.TryGetInt64(out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        return new SearchResult(
            string.IsNullOrWhiteSpace(url) ? DiscussionUrl(id!) : url,
            title.Trim(),
            null,
            Name,
            publishedAt);
    }

    private static string DiscussionUrl(string id) => $"https://news.ycombinator.com/item?id={Uri.EscapeDataString(id)}";
}
