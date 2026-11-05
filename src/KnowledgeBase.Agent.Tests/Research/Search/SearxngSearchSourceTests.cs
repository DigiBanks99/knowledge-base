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
        Options.Create(new ResearchSettings
        {
            SearxngEndpoint = new Uri("http://127.0.0.1:7325"),
            SearxngEngines = ["bing", "duckduckgo", "github"],
            ResultsPerQuery = 2
        }));

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

        Result<SearchAnswer, Error> results = await _source.SearchAsync("atlas framework", Since, CancellationToken.None);

        ItShouldAskForJson();
        ItShouldMapEachResultUpToTheConfiguredNumber();

        void ItShouldAskForJson() =>
            _handler.Requests.ShouldHaveSingleItem().RequestUri!.AbsoluteUri.ShouldBe("http://127.0.0.1:7325/search?q=atlas%20framework&format=json");

        void ItShouldMapEachResultUpToTheConfiguredNumber() => results.Value.Results.ShouldBe(
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

        Result<SearchAnswer, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldSkipThem();

        void ItShouldSkipThem() => results.Value.Results.ShouldHaveSingleItem().Url.ShouldBe("https://example.com/ok");
    }

    [Test]
    public async Task SearxngHasJsonDisabled()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = "Forbidden" };

        Result<SearchAnswer, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldFailWithTheStatus();

        void ItShouldFailWithTheStatus() => results.Error.ShouldBeOfType<SearchSourceError>().ShouldSatisfyAllConditions(
            error => error.Source.ShouldBe("SearXNG"),
            error => error.Message.ShouldContain("403 Forbidden"));
    }

    [Test]
    public async Task SearxngAnswersWithSomethingElse()
    {
        _handler.Respond = _ => StubHttpHandler.Json("""{ "answers": [] }""");

        Result<SearchAnswer, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldFailExplainingWhy();

        void ItShouldFailExplainingWhy() => results.Error.Message.ShouldContain("no `results` array");
    }

    [Test]
    public async Task EveryLoadedEngineFails()
    {
        Serve(config: Config(("bing", true), ("duckduckgo", true)), search: """
            { "results": [], "unresponsive_engines": [ ["bing", "CAPTCHA"], ["duckduckgo", "timeout"] ] }
            """);
        await _source.ProbeAsync(CancellationToken.None);

        Result<SearchAnswer, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldFailNamingTheEnginesAndReasons();

        void ItShouldFailNamingTheEnginesAndReasons() => results.Error.ShouldBeOfType<SearchSourceError>().ShouldSatisfyAllConditions(
            error => error.Message.ShouldContain("bing (CAPTCHA)"),
            error => error.Message.ShouldContain("duckduckgo (timeout)"));
    }

    [Test]
    public async Task OneEngineFailsWhileOthersAnswer()
    {
        _handler.Respond = _ => StubHttpHandler.Json("""
            { "results": [ { "url": "https://example.com/ok", "title": "Ok" } ], "unresponsive_engines": [ ["bing", "CAPTCHA"] ] }
            """);

        Result<SearchAnswer, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldKeepWhatAnswered();
        ItShouldRecordTheEngineThatFailed();

        void ItShouldKeepWhatAnswered() => results.Value.Results.ShouldHaveSingleItem().Url.ShouldBe("https://example.com/ok");

        void ItShouldRecordTheEngineThatFailed() => results.Value.Unanswered.ShouldBe([new EngineFailure("bing", "CAPTCHA")]);
    }

    [Test]
    public async Task OneEngineFailsAndTheOthersFindNothing()
    {
        _handler.Respond = _ => StubHttpHandler.Json("""
            { "results": [], "unresponsive_engines": [ ["bing", "CAPTCHA"] ] }
            """);

        Result<SearchAnswer, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldCountAsSearched();

        void ItShouldCountAsSearched() => results.ShouldSatisfyAllConditions(
            () => results.IsSuccess.ShouldBeTrue(),
            () => results.Value.Results.ShouldBeEmpty(),
            () => results.Value.Unanswered.ShouldBe([new EngineFailure("bing", "CAPTCHA")]));
    }

    [Test]
    public async Task NoExpectedEngineIsLoaded()
    {
        Serve(config: Config(("wikipedia", true)), search: """{ "results": [], "unresponsive_engines": [] }""");
        await _source.ProbeAsync(CancellationToken.None);

        Result<SearchAnswer, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldFail();

        void ItShouldFail() => results.Error.Message.ShouldContain("none of its expected engines is loaded");
    }

    [Test]
    public async Task SearxngHasNothingOnTheTopic()
    {
        _handler.Respond = _ => StubHttpHandler.Json("""{ "results": [], "unresponsive_engines": [] }""");

        Result<SearchAnswer, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldSucceedWithNothing();

        void ItShouldSucceedWithNothing() => results.Value.Results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearxngIsUnreachable()
    {
        _handler.Respond = _ => throw new HttpRequestException("Connection refused");

        Result<SearchAnswer, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);
        UnitResult<Error> probe = await _source.ProbeAsync(CancellationToken.None);

        ItShouldFailTheSearch();
        ItShouldFailTheProbe();

        void ItShouldFailTheSearch() => results.Error.Message.ShouldContain("Connection refused");

        void ItShouldFailTheProbe() => probe.Error.Message.ShouldContain("Connection refused");
    }

    [Test]
    public async Task SearxngLoadsEveryExpectedEngine()
    {
        Serve(config: Config(("bing", true), ("duckduckgo", true), ("github", true), ("wikipedia", false)));

        UnitResult<Error> probe = await _source.ProbeAsync(CancellationToken.None);

        ItShouldAskItsHealthThenItsConfig();
        ItShouldSucceed();

        void ItShouldAskItsHealthThenItsConfig() =>
            _handler.Requests.Select(request => request.RequestUri!.AbsoluteUri).ShouldBe(["http://127.0.0.1:7325/healthz", "http://127.0.0.1:7325/config"]);

        void ItShouldSucceed() => probe.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async Task AnExpectedEngineIsNotLoaded()
    {
        Serve(config: Config(("bing", true), ("duckduckgo", true)));

        UnitResult<Error> probe = await _source.ProbeAsync(CancellationToken.None);

        ItShouldAskWhetherItsKeyIsSet();

        void ItShouldAskWhetherItsKeyIsSet() => probe.Error.ShouldBeOfType<SearchEnginesDegradedError>()
            .Message.ShouldBe("github is not loaded; is its API key set?");
    }

    [Test]
    public async Task SearxngsConfigListsNoEngines()
    {
        Serve(config: """{ "instance_name": "knowledge-base" }""", search: """{ "results": [], "unresponsive_engines": [] }""");

        UnitResult<Error> probe = await _source.ProbeAsync(CancellationToken.None);
        Result<SearchAnswer, Error> results = await _source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldFailTheProbeExplainingWhy();
        ItShouldNotTakeEveryEngineForMissing();

        void ItShouldFailTheProbeExplainingWhy() => probe.Error.ShouldBeOfType<SearchSourceError>().Message.ShouldContain("no `engines` array");

        void ItShouldNotTakeEveryEngineForMissing() => results.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async Task AnExpectedEngineIsDisabled()
    {
        Serve(config: Config(("bing", true), ("duckduckgo", false), ("github", true)));

        UnitResult<Error> probe = await _source.ProbeAsync(CancellationToken.None);

        ItShouldNameIt();

        void ItShouldNameIt() => probe.Error.Message.ShouldBe("duckduckgo is disabled.");
    }

    [Test]
    public async Task AnEngineFailedItsLastSearch()
    {
        Serve(
            config: Config(("bing", true), ("duckduckgo", true), ("github", true)),
            search: """{ "results": [ { "url": "https://example.com/ok", "title": "Ok" } ], "unresponsive_engines": [ ["bing", "access denied"] ] }""");
        await _source.SearchAsync("atlas", Since, CancellationToken.None);

        UnitResult<Error> probe = await _source.ProbeAsync(CancellationToken.None);

        ItShouldShowTheFailure();

        void ItShouldShowTheFailure() => probe.Error.Message.ShouldBe("bing: access denied.");
    }

    [Test]
    public async Task AFailedEngineAnswersAgain()
    {
        Serve(
            config: Config(("bing", true), ("duckduckgo", true), ("github", true)),
            search: """{ "results": [], "unresponsive_engines": [ ["bing", "CAPTCHA"] ] }""");
        await _source.SearchAsync("atlas", Since, CancellationToken.None);
        UnitResult<Error> whileFailing = await _source.ProbeAsync(CancellationToken.None);
        Serve(
            config: Config(("bing", true), ("duckduckgo", true), ("github", true)),
            search: """{ "results": [], "unresponsive_engines": [] }""");
        await _source.SearchAsync("atlas", Since, CancellationToken.None);

        UnitResult<Error> afterwards = await _source.ProbeAsync(CancellationToken.None);

        ItShouldRememberTheFailure();
        ItShouldForgetItOnceTheEngineAnswers();

        void ItShouldRememberTheFailure() => whileFailing.Error.Message.ShouldBe("bing: CAPTCHA.");

        void ItShouldForgetItOnceTheEngineAnswers() => afterwards.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async Task NoEngineIsAnswering()
    {
        Serve(
            config: Config(("bing", true), ("duckduckgo", false)),
            search: """{ "results": [], "unresponsive_engines": [ ["bing", "timeout"] ] }""");
        await _source.SearchAsync("atlas", Since, CancellationToken.None);

        UnitResult<Error> probe = await _source.ProbeAsync(CancellationToken.None);

        ItShouldSaySoAndNameEachProblem();

        void ItShouldSaySoAndNameEachProblem() => probe.Error.Message.ShouldBe(
            "No engine is answering: bing: timeout. duckduckgo is disabled. github is not loaded; is its API key set?");
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
            _handler.Requests.Select(request => request.RequestUri!.AbsoluteUri)
                .ShouldBe(["http://search.internal/searxng/healthz", "http://search.internal/searxng/config"]);
    }

    [Test]
    public async Task NoSearxngIsConfigured()
    {
        SearxngSearchSource source = new(StubHttpHandler.FactoryFor(_handler), Options.Create(new ResearchSettings()));

        Result<SearchAnswer, Error> results = await source.SearchAsync("atlas", Since, CancellationToken.None);

        ItShouldSayItIsNotConfigured();
        ItShouldNotCallAnything();

        void ItShouldSayItIsNotConfigured() => source.ShouldSatisfyAllConditions(
            () => source.IsConfigured.ShouldBeFalse(),
            () => results.Error.Message.ShouldContain("Research:SearxngEndpoint"));

        void ItShouldNotCallAnything() => _handler.Requests.ShouldBeEmpty();
    }

    /// <summary>A <c>/config</c> answer listing <paramref name="engines"/>, in the shape SearXNG gives it.</summary>
    private static string Config(params (string Name, bool Enabled)[] engines) =>
        $$"""{ "engines": [ {{string.Join(", ", engines.Select(engine => $$"""{ "name": "{{engine.Name}}", "enabled": {{(engine.Enabled ? "true" : "false")}}, "categories": ["general"] }"""))}} ] }""";

    /// <summary>Answers the health route, <c>/config</c> with <paramref name="config"/> and searches with <paramref name="search"/>.</summary>
    private void Serve(string config, string search = """{ "results": [] }""") =>
        _handler.Respond = request => request.RequestUri!.AbsolutePath switch
        {
            "/healthz" => new HttpResponseMessage(HttpStatusCode.OK),
            "/config" => StubHttpHandler.Json(config),
            _ => StubHttpHandler.Json(search)
        };
}
