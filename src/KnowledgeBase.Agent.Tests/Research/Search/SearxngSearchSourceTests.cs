using System.Net;

using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Search;

public sealed class SearxngSearchSourceTests : IDisposable
{
    private static readonly DateTimeOffset Since = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly StubHttpHandler _handler = new();
    private readonly SearxngSearchSource _source;

    public SearxngSearchSourceTests() => _source = new SearxngSearchSource(
        StubHttpHandler.FactoryFor(_handler),
        Options.Create(new ResearchSettings { SearxngEndpoint = new Uri("http://127.0.0.1:7325"), ResultsPerQuery = 2 }));

    public void Dispose() => _handler.Dispose();

    [Test]
    public async Task SearxngAnswersAQuery()
    {
        _handler.Respond = _ => StubHttpHandler.Json("""
            {
              "query": "atlas framework",
              "results": [
                { "url": "https://example.com/atlas-2", "title": " Atlas 2 released ", "content": "The second major version.", "publishedDate": "2026-10-20T08:00:00", "engine": "bing" },
                { "url": "https://example.org/atlas", "title": "Atlas", "content": null, "publishedDate": null, "engine": "duckduckgo" },
                { "url": "https://example.net/third", "title": "Third", "engine": "mojeek" }
              ]
            }
            """);

        Result<IReadOnlyList<SearchResult>, Error> results = await _source.SearchAsync("atlas framework", Since, CancellationToken.None);

        ItShouldAskForJson();
        ItShouldMapEachResultUpToTheConfiguredNumber();

        void ItShouldAskForJson() =>
            _handler.Requests.ShouldHaveSingleItem().RequestUri!.AbsoluteUri.ShouldBe("http://127.0.0.1:7325/search?q=atlas%20framework&format=json");

        void ItShouldMapEachResultUpToTheConfiguredNumber() => results.Value.ShouldBe(
        [
            new SearchResult("https://example.com/atlas-2", "Atlas 2 released", "The second major version.", "SearXNG", new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero)),
            new SearchResult("https://example.org/atlas", "Atlas", null, "SearXNG", null)
        ]);
    }

    [Test]
    public async Task SearxngReturnsResultsWithoutAUrlOrTitle()
    {
        _handler.Respond = _ => StubHttpHandler.Json("""
            { "results": [ { "title": "No link" }, { "url": "https://example.com/untitled" }, { "url": "https://example.com/ok", "title": "Ok" } ] }
            """);

        Result<IReadOnlyList<SearchResult>, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldSkipThem();

        void ItShouldSkipThem() => results.Value.ShouldHaveSingleItem().Url.ShouldBe("https://example.com/ok");
    }

    [Test]
    public async Task SearxngHasJsonDisabled()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = "Forbidden" };

        Result<IReadOnlyList<SearchResult>, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldFailWithTheStatus();

        void ItShouldFailWithTheStatus() => results.Error.ShouldBeOfType<SearchSourceError>().ShouldSatisfyAllConditions(
            error => error.Source.ShouldBe("SearXNG"),
            error => error.Message.ShouldContain("403 Forbidden"));
    }

    [Test]
    public async Task SearxngAnswersWithSomethingElse()
    {
        _handler.Respond = _ => StubHttpHandler.Json("""{ "answers": [] }""");

        Result<IReadOnlyList<SearchResult>, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldFailExplainingWhy();

        void ItShouldFailExplainingWhy() => results.Error.Message.ShouldContain("no `results` array");
    }

    [Test]
    public async Task SearxngIsUnreachable()
    {
        _handler.Respond = _ => throw new HttpRequestException("Connection refused");

        Result<IReadOnlyList<SearchResult>, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);
        UnitResult<Error> probe = await _source.ProbeAsync(CancellationToken.None);

        ItShouldFailTheSearch();
        ItShouldFailTheProbe();

        void ItShouldFailTheSearch() => results.Error.Message.ShouldContain("Connection refused");

        void ItShouldFailTheProbe() => probe.Error.Message.ShouldContain("Connection refused");
    }

    [Test]
    public async Task SearxngIsProbed()
    {
        UnitResult<Error> probe = await _source.ProbeAsync(CancellationToken.None);

        ItShouldAskItsHealthRoute();
        ItShouldSucceed();

        void ItShouldAskItsHealthRoute() =>
            _handler.Requests.ShouldHaveSingleItem().RequestUri!.AbsoluteUri.ShouldBe("http://127.0.0.1:7325/healthz");

        void ItShouldSucceed() => probe.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async Task SearxngIsServedUnderAPath()
    {
        SearxngSearchSource source = new(
            StubHttpHandler.FactoryFor(_handler),
            Options.Create(new ResearchSettings { SearxngEndpoint = new Uri("http://search.internal/searxng") }));

        await source.ProbeAsync(CancellationToken.None);

        ItShouldKeepThePath();

        void ItShouldKeepThePath() =>
            _handler.Requests.ShouldHaveSingleItem().RequestUri!.AbsoluteUri.ShouldBe("http://search.internal/searxng/healthz");
    }

    [Test]
    public async Task NoSearxngIsConfigured()
    {
        SearxngSearchSource source = new(StubHttpHandler.FactoryFor(_handler), Options.Create(new ResearchSettings()));

        Result<IReadOnlyList<SearchResult>, Error> results = await source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldSayItIsNotConfigured();
        ItShouldNotCallAnything();

        void ItShouldSayItIsNotConfigured() => source.ShouldSatisfyAllConditions(
            () => source.IsConfigured.ShouldBeFalse(),
            () => results.Error.Message.ShouldContain("Research:SearxngEndpoint"));

        void ItShouldNotCallAnything() => _handler.Requests.ShouldBeEmpty();
    }
}
