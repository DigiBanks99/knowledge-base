using System.Net;
using System.Text;

using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Research.Curation;

using Microsoft.Extensions.Configuration;

namespace KnowledgeBase.Agent.Tests.Research.Curation;

public sealed class LlamaServerTokenCounterTests : IDisposable
{
    private readonly StubHandler _handler = new();
    private readonly ConfigurationManager _configuration = new();
    private readonly LlamaServerTokenCounter _counter;

    public LlamaServerTokenCounterTests()
    {
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(LlamaServerTokenCounter.HttpClientName).Returns(_ => new HttpClient(_handler, disposeHandler: false));
        _configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{OpenAICompatibleOptions.SectionName}:Endpoint"] = "http://localhost:8080/v1",
            [$"{OpenAICompatibleOptions.SectionName}:ApiKey"] = "a-key",
            [$"{OpenAICompatibleOptions.SectionName}:Model"] = "qwen3.5-4b"
        });
        _counter = new LlamaServerTokenCounter(httpClientFactory, _configuration);
    }

    public void Dispose()
    {
        _handler.Dispose();
        _configuration.Dispose();
    }

    [Test]
    public async Task TheServerTokenizesThePrompt()
    {
        _handler.Respond = _ => Json("""{"tokens":[1,2,3,4]}""");

        Result<int, Error> result = await _counter.CountAsync("Curate the topic.", CancellationToken.None);

        ItShouldCountTheTokens();
        ItShouldAskTheTokenizeRoute();
        ItShouldSendThePromptForTheConfiguredModel();

        void ItShouldCountTheTokens() => result.Value.ShouldBe(4);

        void ItShouldAskTheTokenizeRoute() => _handler.Requests.Single().ShouldSatisfyAllConditions(
            request => request.Method.ShouldBe(HttpMethod.Post),
            request => request.RequestUri.ShouldBe(new Uri("http://localhost:8080/tokenize")),
            request => request.Headers.Authorization.ShouldNotBeNull().ToString().ShouldBe("Bearer a-key"));

        void ItShouldSendThePromptForTheConfiguredModel() =>
            _handler.Bodies.Single().ShouldBe("""{"content":"Curate the topic.","model":"qwen3.5-4b"}""");
    }

    [Test]
    public async Task TheServerIsUnreachable()
    {
        _handler.Respond = _ => throw new HttpRequestException("Connection refused");

        Result<int, Error> result = await _counter.CountAsync("Curate the topic.", CancellationToken.None);

        ItShouldBeUnavailable();

        void ItShouldBeUnavailable() => result.Error.ShouldBeOfType<TokenCountUnavailableError>()
            .Message.ShouldContain("http://localhost:8080/tokenize: Connection refused");
    }

    [Test]
    public async Task TheServerAnswersWithAnError()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        Result<int, Error> result = await _counter.CountAsync("Curate the topic.", CancellationToken.None);

        ItShouldBeUnavailable();

        void ItShouldBeUnavailable() => result.Error.ShouldBeOfType<TokenCountUnavailableError>()
            .Message.ShouldContain("answered 503");
    }

    [Test]
    public async Task TheServerAnswersWithoutTokens()
    {
        _handler.Respond = _ => Json("""{"error":"nope"}""");

        Result<int, Error> result = await _counter.CountAsync("Curate the topic.", CancellationToken.None);

        ItShouldBeUnavailable();

        void ItShouldBeUnavailable() => result.Error.ShouldBeOfType<TokenCountUnavailableError>();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return Respond(request);
        }
    }
}
