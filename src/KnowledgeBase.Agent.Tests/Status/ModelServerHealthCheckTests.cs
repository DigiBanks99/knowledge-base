using System.Net;

using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Status;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Tests.Status;

public sealed class ModelServerHealthCheckTests : IDisposable
{
    private readonly StubHandler _handler = new();
    private readonly ConfigurationManager _configuration = new();
    private readonly ModelServerHealthCheck _check;
    private readonly HealthCheckContext _context = new()
    {
        Registration = new HealthCheckRegistration(
            "Model server",
            Substitute.For<IHealthCheck>(),
            HealthStatus.Degraded,
            [StatusDependencyInjection.StatusTag])
    };

    public ModelServerHealthCheckTests()
    {
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(ModelServerHealthCheck.HttpClientName).Returns(_ => new HttpClient(_handler, disposeHandler: false));

        _configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{KnownAgents.Librarian}:Provider"] = nameof(ModelProvider.OpenAICompatible),
            [$"{KnownAgents.FactFinder}:Provider"] = nameof(ModelProvider.Anthropic),
            [$"{OpenAICompatibleOptions.SectionName}:Endpoint"] = "http://localhost:8080/v1",
            [$"{OpenAICompatibleOptions.SectionName}:ApiKey"] = "a-key"
        });

        _check = new ModelServerHealthCheck(httpClientFactory, _configuration);
    }

    public void Dispose()
    {
        _handler.Dispose();
        _configuration.Dispose();
    }

    [Test]
    [Arguments("http://localhost:8080/v1", "http://localhost:8080/health")]
    [Arguments("http://localhost:8080/v1/", "http://localhost:8080/health")]
    [Arguments("http://models.internal/llama/v1", "http://models.internal/llama/health")]
    [Arguments("http://localhost:8080", "http://localhost:8080/health")]
    public void AnEndpointIsConfigured(string endpoint, string expectedHealthUri)
    {
        Uri healthUri = ModelServerHealthCheck.HealthUri(new Uri(endpoint));

        ItShouldCheckTheServersHealthRoute();

        void ItShouldCheckTheServersHealthRoute() => healthUri.ShouldBe(new Uri(expectedHealthUri));
    }

    [Test]
    public async Task TheModelServerIsUp()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);

        HealthCheckResult result = await _check.CheckHealthAsync(_context, CancellationToken.None);

        ItShouldBeHealthy();
        ItShouldAskTheHealthRoute();
        ItShouldSendTheApiKey();

        void ItShouldBeHealthy() => result.Status.ShouldBe(HealthStatus.Healthy);

        void ItShouldAskTheHealthRoute() => _handler.Requests.Single().RequestUri.ShouldBe(new Uri("http://localhost:8080/health"));

        void ItShouldSendTheApiKey() => _handler.Requests.Single().Headers.Authorization.ShouldNotBeNull().ToString().ShouldBe("Bearer a-key");
    }

    [Test]
    public async Task TheModelServerIsStillLoadingItsModel()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        HealthCheckResult result = await _check.CheckHealthAsync(_context, CancellationToken.None);

        ItShouldBeDegraded();
        ItShouldNameTheStatusCode();

        void ItShouldBeDegraded() => result.Status.ShouldBe(HealthStatus.Degraded);

        void ItShouldNameTheStatusCode() => result.Description.ShouldBe("http://localhost:8080/health answered 503 Service Unavailable.");
    }

    [Test]
    public async Task TheModelServerCannotBeReached()
    {
        _handler.Respond = _ => throw new HttpRequestException("Connection refused");

        HealthCheckResult result = await _check.CheckHealthAsync(_context, CancellationToken.None);

        ItShouldBeDegraded();
        ItShouldSayWhy();

        void ItShouldBeDegraded() => result.Status.ShouldBe(HealthStatus.Degraded);

        void ItShouldSayWhy() => result.Description.ShouldBe("Unreachable at http://localhost:8080/health: Connection refused");
    }

    [Test]
    public async Task TheModelServerTimesOut()
    {
        _handler.Respond = _ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

        HealthCheckResult result = await _check.CheckHealthAsync(_context, CancellationToken.None);

        ItShouldBeDegraded();

        void ItShouldBeDegraded() => result.Status.ShouldBe(HealthStatus.Degraded);
    }

    [Test]
    public async Task NoAgentUsesTheModelServer()
    {
        _configuration[$"{KnownAgents.Librarian}:Provider"] = nameof(ModelProvider.Anthropic);

        HealthCheckResult result = await _check.CheckHealthAsync(_context, CancellationToken.None);

        ItShouldBeHealthy();
        ItShouldNotCallAnything();

        void ItShouldBeHealthy() => result.Status.ShouldBe(HealthStatus.Healthy);

        void ItShouldNotCallAnything() => _handler.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task AnAgentUsesTheModelServerButNoEndpointIsConfigured()
    {
        _configuration[$"{OpenAICompatibleOptions.SectionName}:Endpoint"] = null;

        HealthCheckResult result = await _check.CheckHealthAsync(_context, CancellationToken.None);

        ItShouldBeDegraded();
        ItShouldNotCallAnything();

        void ItShouldBeDegraded() => result.Status.ShouldBe(HealthStatus.Degraded);

        void ItShouldNotCallAnything() => _handler.Requests.ShouldBeEmpty();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }
}