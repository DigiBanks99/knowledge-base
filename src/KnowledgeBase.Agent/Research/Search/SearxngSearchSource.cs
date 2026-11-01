using System.Globalization;
using System.Text.Json;

using KnowledgeBase.Agent.Research.Models;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Search;

/// <summary>
/// The self-hosted SearXNG metasearch instance, through its JSON output. It cannot filter by date across engines, so
/// recency is left to the result filter.
/// </summary>
internal sealed class SearxngSearchSource(IHttpClientFactory httpClientFactory, IOptions<ResearchSettings> settings)
    : ISearchSource
{
    public const string HttpClientName = "Searxng";
    public const string SourceName = "SearXNG";

    private readonly ResearchSettings _settings = settings.Value;

    public string Name => SourceName;

    public bool IsConfigured => _settings.SearxngEndpoint is not null;

    public async Task<Result<IReadOnlyList<SearchResult>, Error>> SearchAsync(
        string query,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        if (_settings.SearxngEndpoint is null)
        {
            return NotConfigured();
        }

        Uri uri = SearchHttp.Route(_settings.SearxngEndpoint, $"search?q={Uri.EscapeDataString(query)}&format=json");
        Result<JsonDocument, Error> answer = await SearchHttp.GetJsonAsync(httpClientFactory.CreateClient(HttpClientName), uri, Name, cancellationToken);
        if (answer.IsFailure)
        {
            return answer.Error;
        }

        using JsonDocument body = answer.Value;
        if (!body.RootElement.TryGetProperty("results", out JsonElement results) || results.ValueKind != JsonValueKind.Array)
        {
            return new SearchSourceError(Name, "the answer has no `results` array; is JSON enabled in its search formats?");
        }

        // An instance whose engines are all blocked (CAPTCHA, timeouts, rate limits) still answers 200 with no results
        // and names the failures here. Reporting that as an empty success would mark the topic searched.
        if (results.GetArrayLength() == 0)
        {
            string[] unresponsive = UnresponsiveEngines(body.RootElement);
            if (unresponsive.Length > 0)
            {
                return new SearchSourceError(Name, $"no results, and these engines did not answer: {string.Join(", ", unresponsive)}.");
            }
        }

        return Result.Success<IReadOnlyList<SearchResult>, Error>(
        [
            .. results.EnumerateArray()
                .Select(ToResult)
                .Choose()
                .Take(Math.Max(0, _settings.ResultsPerQuery))
        ]);
    }

    public async Task<UnitResult<Error>> ProbeAsync(CancellationToken cancellationToken)
    {
        if (_settings.SearxngEndpoint is null)
        {
            return NotConfigured();
        }

        Uri uri = SearchHttp.Route(_settings.SearxngEndpoint, "healthz");
        try
        {
            using HttpResponseMessage response = await httpClientFactory.CreateClient(HttpClientName).GetAsync(uri, cancellationToken);
            return response.IsSuccessStatusCode
                ? UnitResult.Success<Error>()
                : new SearchSourceError(Name, $"{uri} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new SearchSourceError(Name, $"{uri}: {ex.Message}");
        }
    }

    /// <summary>Each failed engine as <c>name (reason)</c>, from the answer's <c>unresponsive_engines</c> pairs.</summary>
    private static string[] UnresponsiveEngines(JsonElement root) =>
        root.TryGetProperty("unresponsive_engines", out JsonElement engines) && engines.ValueKind == JsonValueKind.Array
            ? [
                .. engines.EnumerateArray()
                    .Where(engine => engine.ValueKind == JsonValueKind.Array && engine.GetArrayLength() > 0)
                    .Select(engine => engine.GetArrayLength() > 1 ? $"{engine[0]} ({engine[1]})" : engine[0].ToString())
            ]
            : [];

    private SearchSourceError NotConfigured() =>
        new(Name, $"'{ResearchSettings.SectionName}:{nameof(ResearchSettings.SearxngEndpoint)}' is not set.");

    private Maybe<SearchResult> ToResult(JsonElement result)
    {
        string? url = SearchHttp.String(result, "url");
        string? title = SearchHttp.String(result, "title");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title))
        {
            return Maybe.None;
        }

        DateTimeOffset? publishedAt = DateTimeOffset.TryParse(
            SearchHttp.String(result, "publishedDate"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset published)
            ? published
            : null;
        return new SearchResult(url, title.Trim(), SearchHttp.String(result, "content")?.Trim(), Name, publishedAt);
    }
}
