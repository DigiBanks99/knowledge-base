using System.Net;
using System.Text.Json.Nodes;

using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Tests.Research.Search;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Triage;

public sealed class LlamaServerKeeperTests : IDisposable
{
    private static readonly KeeperPage Page = new(
        "News about the Atlas framework.",
        "Atlas 2 is out",
        "https://example.com/atlas-2",
        ["Atlas 2 ships a new scheduler and drops the old plugin API.", "Upgrading takes an afternoon for most projects."],
        50);

    private readonly StubHttpHandler _handler = new();
    private readonly List<JsonNode> _bodies = [];
    private readonly ConfigurationManager _configuration = new();
    private readonly LlamaServerKeeper _keeper;

    public LlamaServerKeeperTests()
    {
        _configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{OpenAICompatibleOptions.SectionName}:Endpoint"] = "http://localhost:8080/v1",
            [$"{OpenAICompatibleOptions.SectionName}:ApiKey"] = "a-key"
        });
        _keeper = Keeper(new ResearchSettings());
    }

    public void Dispose()
    {
        _handler.Dispose();
        _configuration.Dispose();
    }

    [Test]
    public async Task AResultIsScreened()
    {
        Answer("""{"answers":{"useful":{"noul":0.72},"injection":{"noul":0.1}},"usage":{"input_tokens":812}}""");

        Result<Screening, Error> result = await _keeper.ScreenAsync(Page, CancellationToken.None);

        ItShouldAskTheSharedServersSystemOneRoute();
        ItShouldNameTheKeepersModel();
        ItShouldSendTheTopicAndNumberedParagraphsAsState();
        ItShouldAskOnlyUsefulAndInjection();
        ItShouldReadTheProbabilities();

        void ItShouldAskTheSharedServersSystemOneRoute() => _handler.Requests.Single().ShouldSatisfyAllConditions(
            request => request.Method.ShouldBe(HttpMethod.Post),
            request => request.RequestUri.ShouldBe(new Uri("http://localhost:8080/v1/systemone")),
            request => request.Headers.Authorization.ShouldNotBeNull().ToString().ShouldBe("Bearer a-key"));

        void ItShouldNameTheKeepersModel() => ((string?)_bodies.Single()["model"]).ShouldBe("laya");

        void ItShouldSendTheTopicAndNumberedParagraphsAsState() => _bodies.Single()["state"]!.ToJsonString().ShouldBe(
            """{"topic":"News about the Atlas framework.","result":{"title":"Atlas 2 is out","url":"https://example.com/atlas-2","paragraphs":{"1":"Atlas 2 ships a new scheduler and drops the old plugin API.","2":"Upgrading takes an afternoon for most projects."}}}""");

        void ItShouldAskOnlyUsefulAndInjection() => _bodies.Single()["questions"]!.AsObject()
            .Select(question => (question.Key, (string?)question.Value!["type"]))
            .ShouldBe([("useful", "noul"), ("injection", "noul")]);

        void ItShouldReadTheProbabilities() => result.Value.ShouldBe(new Screening(0.72, 0.1));
    }

    [Test]
    public async Task AKeptResultIsDescribed()
    {
        Answer("""{"answers":{"kind":{"choice":"release","probabilities":{"release":0.81,"spam":0.02},"confidence":0.7},"quotes":{"choice":"1","probabilities":{"1":0.95,"2":0.05}}}}""");

        Result<Description, Error> result = await _keeper.DescribeAsync(Page, CancellationToken.None);

        ItShouldOfferEveryKindWithItsDescription();
        ItShouldOfferTheParagraphNumbersAsQuotes();
        ItShouldReadTheKindAndQuoteProbabilities();

        void ItShouldOfferEveryKindWithItsDescription() => _bodies.Single()["questions"]!["kind"]!["criteria"]!.AsObject()
            .Select(option => option.Key)
            .ShouldBe(["release", "announcement", "discussion", "tutorial", "opinion", "beginner_question", "spam", "unrelated"]);

        void ItShouldOfferTheParagraphNumbersAsQuotes() => _bodies.Single()["questions"]!["quotes"]!.ShouldSatisfyAllConditions(
            quotes => ((string?)quotes["type"]).ShouldBe("choice"),
            quotes => quotes["criteria"]!.AsObject().Select(option => option.Key).ShouldBe(["1", "2"]));

        void ItShouldReadTheKindAndQuoteProbabilities() => result.Value.ShouldSatisfyAllConditions(
            description => description.Kind.ShouldBe("release"),
            description => description.KindProbability.ShouldBe(0.81),
            description => description.QuoteProbabilities.ShouldBe(new Dictionary<int, double> { [1] = 0.95, [2] = 0.05 }));
    }

    [Test]
    public async Task APageHasOneParagraph()
    {
        Answer("""{"answers":{"kind":{"choice":"opinion","probabilities":{"opinion":0.6}}}}""");

        Result<Description, Error> result = await _keeper.DescribeAsync(Page with { Paragraphs = [Page.Paragraphs[0]] }, CancellationToken.None);

        ItShouldNotAskForQuotes();
        ItShouldStillDescribeTheKind();

        void ItShouldNotAskForQuotes() => _bodies.Single()["questions"]!.AsObject().Select(question => question.Key).ShouldBe(["kind"]);

        void ItShouldStillDescribeTheKind() => result.Value.ShouldSatisfyAllConditions(
            description => description.Kind.ShouldBe("opinion"),
            description => description.QuoteProbabilities.ShouldBeEmpty());
    }

    [Test]
    public async Task TheKeeperHasItsOwnServer()
    {
        LlamaServerKeeper keeper = Keeper(new ResearchSettings { KeeperEndpoint = new Uri("http://127.0.0.1:9932/v1"), KeeperApiKey = "own-key", KeeperModel = "laya-q8" });
        Answer("""{"answers":{"useful":{"noul":0.5},"injection":{"noul":0.2}}}""");

        await keeper.ScreenAsync(Page, CancellationToken.None);

        ItShouldAskThatServerWithItsKey();
        ItShouldNameTheConfiguredModel();

        void ItShouldAskThatServerWithItsKey() => _handler.Requests.Single().ShouldSatisfyAllConditions(
            request => request.RequestUri.ShouldBe(new Uri("http://127.0.0.1:9932/v1/systemone")),
            request => request.Headers.Authorization.ShouldNotBeNull().ToString().ShouldBe("Bearer own-key"));

        void ItShouldNameTheConfiguredModel() => ((string?)_bodies.Single()["model"]).ShouldBe("laya-q8");
    }

    [Test]
    public async Task TheServerRefusesTheRequest()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("the request exceeds the context size") };

        Result<Screening, Error> result = await _keeper.ScreenAsync(Page, CancellationToken.None);

        ItShouldFailWithTheServersAnswer();

        void ItShouldFailWithTheServersAnswer() => result.Error.ShouldBeOfType<KeeperCallFailedError>()
            .Message.ShouldBe("The Keeper could not answer: http://localhost:8080/v1/systemone answered 400 Bad Request: the request exceeds the context size");
    }

    [Test]
    public async Task TheServerTakesTooLong()
    {
        LlamaServerKeeper keeper = Keeper(new ResearchSettings { KeeperTimeout = TimeSpan.FromMilliseconds(50) });
        _handler.RespondAsync = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        Result<Screening, Error> result = await keeper.ScreenAsync(Page, CancellationToken.None);

        ItShouldFailWithTheTimeout();

        void ItShouldFailWithTheTimeout() => result.Error.ShouldBeOfType<KeeperCallFailedError>()
            .Message.ShouldBe("The Keeper could not answer: http://localhost:8080/v1/systemone did not answer within 0 seconds.");
    }

    [Test]
    public async Task TheAnswerLacksAProbability()
    {
        Answer("""{"answers":{"useful":{"noul":0.5}}}""");

        Result<Screening, Error> result = await _keeper.ScreenAsync(Page, CancellationToken.None);

        ItShouldFailNamingTheMissingAnswer();

        void ItShouldFailNamingTheMissingAnswer() => result.Error.ShouldBeOfType<KeeperCallFailedError>()
            .Message.ShouldBe("The Keeper could not answer: the answer has no `injection.noul` probability.");
    }

    [Test]
    [Arguments("""{"answers":[0.5,0.2]}""")]
    [Arguments("""{"answers":{"kind":{"choice":3}}}""")]
    [Arguments("""{"answers":{"kind":"release"}}""")]
    public async Task TheAnswerHasAnUnexpectedShape(string json)
    {
        Answer(json);

        Result<Description, Error> result = await _keeper.DescribeAsync(Page, CancellationToken.None);

        ItShouldFailAsTheKeepersError();

        void ItShouldFailAsTheKeepersError() => result.Error.ShouldBeOfType<KeeperCallFailedError>();
    }

    [Test]
    public async Task NoServerIsConfigured()
    {
        _configuration[$"{OpenAICompatibleOptions.SectionName}:Endpoint"] = null;

        Result<Screening, Error> result = await _keeper.ScreenAsync(Page, CancellationToken.None);

        ItShouldFailWithoutAsking();

        void ItShouldFailWithoutAsking()
        {
            _handler.Requests.ShouldBeEmpty();
            result.Error.ShouldBeOfType<KeeperCallFailedError>().Message.ShouldContain("'Research:KeeperEndpoint'");
        }
    }

    private LlamaServerKeeper Keeper(ResearchSettings settings) =>
        new(StubHttpHandler.FactoryFor(_handler), Options.Create(settings), _configuration, TimeProvider.System);

    /// <summary>Answers every request with <paramref name="json"/>, keeping each request's body to inspect.</summary>
    private void Answer(string json) => _handler.RespondAsync = async (request, cancellationToken) =>
    {
        _bodies.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!);
        return StubHttpHandler.Json(json);
    };
}
