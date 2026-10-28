using System.Net;

using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Search;

public sealed class HackerNewsSearchSourceTests : IDisposable
{
    private static readonly DateTimeOffset Since = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly StubHttpHandler _handler = new();
    private readonly HackerNewsSearchSource _source;

    public HackerNewsSearchSourceTests() => _source = new HackerNewsSearchSource(
        StubHttpHandler.FactoryFor(_handler),
        Options.Create(new ResearchSettings { ResultsPerQuery = 5 }));

    public void Dispose() => _handler.Dispose();

    [Test]
    public async Task HackerNewsAnswersAQuery()
    {
        _handler.Respond = _ => StubHttpHandler.Json("""
            {
              "hits": [
                { "objectID": "41000001", "title": "Atlas 2 is out", "url": "https://example.com/atlas-2", "created_at_i": 1761552000 },
                { "objectID": "41000002", "title": "Ask HN: Is anyone using Atlas?", "url": null, "created_at_i": 1761465600 },
                { "objectID": "41000003", "url": "https://example.com/untitled", "created_at_i": 1761465600 }
              ]
            }
            """);

        Result<IReadOnlyList<SearchResult>, Error> results = await _source.SearchAsync("atlas framework", Since, CancellationToken.None);

        ItShouldAskForStoriesNewerThanTheCutoff();
        ItShouldMapEachStory();

        void ItShouldAskForStoriesNewerThanTheCutoff() =>
            _handler.Requests.ShouldHaveSingleItem().RequestUri!.AbsoluteUri.ShouldBe(
                "https://hn.algolia.com/api/v1/search_by_date?query=atlas%20framework&tags=story&hitsPerPage=5&numericFilters=created_at_i%3E1790812800");

        void ItShouldMapEachStory() => results.Value.ShouldBe(
        [
            new SearchResult("https://example.com/atlas-2", "Atlas 2 is out", null, "Hacker News", DateTimeOffset.FromUnixTimeSeconds(1761552000)),
            new SearchResult("https://news.ycombinator.com/item?id=41000002", "Ask HN: Is anyone using Atlas?", null, "Hacker News", DateTimeOffset.FromUnixTimeSeconds(1761465600))
        ]);
    }

    [Test]
    public async Task TheEndpointIsConfiguredWithoutATrailingSlash()
    {
        HackerNewsSearchSource source = new(
            StubHttpHandler.FactoryFor(_handler),
            Options.Create(new ResearchSettings { HackerNewsEndpoint = new Uri("https://hn.algolia.com/api/v1") }));

        await source.ProbeAsync(CancellationToken.None);

        ItShouldKeepItsLastSegment();

        void ItShouldKeepItsLastSegment() =>
            _handler.Requests.ShouldHaveSingleItem().RequestUri!.AbsoluteUri.ShouldBe("https://hn.algolia.com/api/v1/search?tags=story&hitsPerPage=1");
    }

    [Test]
    public async Task HackerNewsIsDown()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { ReasonPhrase = "Service Unavailable" };

        Result<IReadOnlyList<SearchResult>, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);
        UnitResult<Error> probe = await _source.ProbeAsync(CancellationToken.None);

        ItShouldFailTheSearch();
        ItShouldFailTheProbe();

        void ItShouldFailTheSearch() => results.Error.ShouldBeOfType<SearchSourceError>().ShouldSatisfyAllConditions(
            error => error.Source.ShouldBe("Hacker News"),
            error => error.Message.ShouldContain("503 Service Unavailable"));

        void ItShouldFailTheProbe() => probe.IsFailure.ShouldBeTrue();
    }

    [Test]
    public async Task HackerNewsAnswersWithSomethingElse()
    {
        _handler.Respond = _ => StubHttpHandler.Json("not json");

        Result<IReadOnlyList<SearchResult>, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldFail();

        void ItShouldFail() => results.Error.ShouldBeOfType<SearchSourceError>();
    }
}
