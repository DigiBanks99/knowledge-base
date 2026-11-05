using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

using KnowledgeBase.Agent.Research.Models;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Search;

/// <summary>
/// The self-hosted SearXNG metasearch instance, through its JSON output. It cannot filter by date across engines, so
/// recency is left to the result filter.
/// </summary>
/// <remarks>
/// SearXNG answers 200 when its engines fail, naming them in <c>unresponsive_engines</c>, and an engine that never
/// loaded (an unset API key) is not mentioned at all. So the source remembers each engine's last failure, and its probe
/// reads <c>/config</c> for the engines that loaded; both feed the status signal. Both are kept in memory only: a
/// restart clears them and the next probe and search fill them again.
/// </remarks>
internal sealed class SearxngSearchSource(IHttpClientFactory httpClientFactory, IOptions<ResearchSettings> settings)
    : ISearchSource
{
    public const string HttpClientName = "Searxng";
    public const string SourceName = "SearXNG";

    private readonly ResearchSettings _settings = settings.Value;

    // Each engine's reason from the latest search that named it unresponsive, cleared by a later search that doesn't.
    private readonly ConcurrentDictionary<string, string> _lastFailures = new(StringComparer.OrdinalIgnoreCase);

    // The expected engines the latest /config read saw loaded and enabled; until a probe has run, all of them.
    private volatile IReadOnlySet<string>? _loaded;

    public string Name => SourceName;

    public bool IsConfigured => _settings.SearxngEndpoint is not null;

    public async Task<Result<SearchAnswer, Error>> SearchAsync(
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

        EngineFailure[] unresponsive = UnresponsiveEngines(body.RootElement);
        Remember(unresponsive);

        // An empty answer only fails when every engine that could have answered didn't: then nothing is known and the
        // topic should stay due. One engine down still leaves a search worth counting, so a topic isn't searched again
        // (and paid for again) every cycle while it is out.
        if (results.GetArrayLength() == 0 && EveryLoadedEngineFailed(unresponsive))
        {
            return new SearchSourceError(
                Name,
                unresponsive.Length > 0
                    ? $"no results, and these engines did not answer: {string.Join(", ", unresponsive)}."
                    : "no results, and none of its expected engines is loaded.");
        }

        return new SearchAnswer(
            [
                .. results.EnumerateArray()
                    .Select(ToResult)
                    .Choose()
                    .Take(Math.Max(0, _settings.ResultsPerQuery))
            ],
            unresponsive);
    }

    /// <summary>
    /// Whether the instance answers and every expected engine is loaded, enabled and not failing. Reading
    /// <c>/config</c> costs no searches.
    /// </summary>
    public async Task<UnitResult<Error>> ProbeAsync(CancellationToken cancellationToken)
    {
        if (_settings.SearxngEndpoint is null)
        {
            return NotConfigured();
        }

        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        Uri health = SearchHttp.Route(_settings.SearxngEndpoint, "healthz");
        try
        {
            using HttpResponseMessage response = await client.GetAsync(health, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new SearchSourceError(Name, $"{health} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new SearchSourceError(Name, $"{health}: {ex.Message}");
        }

        Result<JsonDocument, Error> config = await SearchHttp.GetJsonAsync(
            client,
            SearchHttp.Route(_settings.SearxngEndpoint, "config"),
            Name,
            cancellationToken);
        if (config.IsFailure)
        {
            return config.Error;
        }

        using JsonDocument body = config.Value;
        // An answer of another shape says nothing about the engines; reading it as "none loaded" would fail every
        // empty search from then on.
        if (Engines(body.RootElement) is not { } engines)
        {
            return new SearchSourceError(Name, "its `/config` answer has no `engines` array.");
        }

        IReadOnlyList<string> expected = _settings.ExpectedSearxngEngines;
        _loaded = new HashSet<string>(
            expected.Where(engine => engines.GetValueOrDefault(engine)),
            StringComparer.OrdinalIgnoreCase);

        string[] problems = [.. expected.Select(engine => Problem(engine, engines)).Choose()];
        if (problems.Length == 0)
        {
            return UnitResult.Success<Error>();
        }

        // Status signals top out at degraded, so the message says when it is all of them.
        string described = string.Join(" ", problems);
        return new SearchEnginesDegradedError(
            problems.Length == expected.Count ? $"No engine is answering: {described}" : described);
    }

    /// <summary>What is wrong with an expected engine, if anything: not loaded, disabled or failing its last search.</summary>
    private Maybe<string> Problem(string engine, Dictionary<string, bool> engines)
    {
        if (!engines.TryGetValue(engine, out bool enabled))
        {
            return $"{engine} is not loaded; is its API key set?";
        }

        if (!enabled)
        {
            return $"{engine} is disabled.";
        }

        return _lastFailures.TryGetValue(engine, out string? reason)
            ? $"{engine}: {(string.IsNullOrWhiteSpace(reason) ? "did not answer" : reason)}."
            : Maybe<string>.None;
    }

    /// <summary>
    /// Stores each expected engine's outcome from this answer. Queries run at once, so the last answer to arrive
    /// decides; that is the "last outcome" the signal shows. Only expected engines are kept, as only they are reported.
    /// </summary>
    private void Remember(EngineFailure[] unresponsive)
    {
        Dictionary<string, string> failed = Failed(unresponsive);
        foreach (string engine in _settings.ExpectedSearxngEngines)
        {
            if (failed.TryGetValue(engine, out string? reason))
            {
                _lastFailures[engine] = reason;
            }
            else
            {
                _lastFailures.TryRemove(engine, out _);
            }
        }
    }

    /// <summary>
    /// Whether every expected engine the latest probe saw loaded failed. With none loaded nothing could answer, so an
    /// empty answer is a failure then too.
    /// </summary>
    private bool EveryLoadedEngineFailed(EngineFailure[] unresponsive)
    {
        Dictionary<string, string> failed = Failed(unresponsive);
        return (_loaded ?? (IEnumerable<string>)_settings.ExpectedSearxngEngines).All(failed.ContainsKey);
    }

    private static Dictionary<string, string> Failed(EngineFailure[] unresponsive) =>
        unresponsive
            .DistinctBy(failure => failure.Engine, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(failure => failure.Engine, failure => failure.Reason, StringComparer.OrdinalIgnoreCase);

    /// <summary>Each engine <c>/config</c> lists, by name, and whether it is enabled; null when it lists none.</summary>
    private static Dictionary<string, bool>? Engines(JsonElement root)
    {
        if (!root.TryGetProperty("engines", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        Dictionary<string, bool> engines = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement engine in list.EnumerateArray())
        {
            if (SearchHttp.String(engine, "name") is { Length: > 0 } name)
            {
                engines[name] = engine.TryGetProperty("enabled", out JsonElement enabled) && enabled.ValueKind == JsonValueKind.True;
            }
        }

        return engines;
    }

    /// <summary>Each failed engine and its reason, from the answer's <c>unresponsive_engines</c> pairs.</summary>
    private static EngineFailure[] UnresponsiveEngines(JsonElement root) =>
        root.TryGetProperty("unresponsive_engines", out JsonElement engines) && engines.ValueKind == JsonValueKind.Array
            ? [
                .. engines.EnumerateArray()
                    .Where(engine => engine.ValueKind == JsonValueKind.Array && engine.GetArrayLength() > 0)
                    .Select(engine => new EngineFailure(engine[0].ToString(), engine.GetArrayLength() > 1 ? engine[1].ToString() : string.Empty))
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
