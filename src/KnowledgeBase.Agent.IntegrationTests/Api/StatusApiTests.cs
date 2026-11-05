using System.Net;

using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Status;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

/// <summary>No agent of the test API uses the OpenAI-compatible provider, so the model-server signal reports itself unused.</summary>
public sealed class StatusApiTests : IDisposable
{
    private readonly TestApi _api = new();

    public void Dispose() => _api.Dispose();

    [Test]
    public async Task TheStatusIsRequested()
    {
        HttpResponseMessage response = await _api.Client().GetAsync("/api/status");
        StatusReport report = await TestApi.ReadAsync<StatusReport>(response);
        // The checks /health runs, minus Redis, which the tests do not start and which takes seconds to give up.
        HealthReport health = await _api.Get<HealthCheckService>()
            .CheckHealthAsync(registration => !registration.Name.Contains("redis", StringComparison.OrdinalIgnoreCase), CancellationToken.None);

        ItShouldSucceed();
        ItShouldReportTheModelServer();
        ItShouldNotBeDegraded();
        ItShouldAgreeWithTheHealthCheck();

        void ItShouldSucceed() => response.StatusCode.ShouldBe(HttpStatusCode.OK);

        void ItShouldReportTheModelServer() => report.Signals.ShouldContain(signal =>
            signal.Name == "Model server"
            && signal.State == StatusSignalState.Healthy
            && signal.Detail!.StartsWith("Not used"));

        void ItShouldNotBeDegraded() => report.IsDegraded.ShouldBeFalse();

        void ItShouldAgreeWithTheHealthCheck() => report.Signals.ShouldAllBe(signal =>
            health.Entries.ContainsKey(signal.Name)
            && health.Entries[signal.Name].Status.ToString() == signal.State.ToString()
            && health.Entries[signal.Name].Description == signal.Detail);
    }

    [Test]
    public async Task SearxngIsMissingAnEngine()
    {
        using TestApi api = new(new Dictionary<string, string?>
        {
            [$"{ResearchSettings.SectionName}:{nameof(ResearchSettings.SearxngEndpoint)}"] = "http://127.0.0.1:7325",
            [$"{ResearchSettings.SectionName}:{nameof(ResearchSettings.SearxngEngines)}:0"] = "bing",
            [$"{ResearchSettings.SectionName}:{nameof(ResearchSettings.SearxngEngines)}:1"] = "github"
        });
        api.Web.Respond = request => request.RequestUri!.AbsolutePath switch
        {
            "/healthz" => new HttpResponseMessage(HttpStatusCode.OK),
            "/config" => FakeWeb.Json("""{ "engines": [ { "name": "bing", "enabled": true }, { "name": "wikipedia", "enabled": true } ] }"""),
            _ => FakeWeb.Json("""{ "hits": [] }""")
        };

        StatusReport report = await TestApi.ReadAsync<StatusReport>(await api.Client().GetAsync("/api/status"));

        ItShouldBeDegradedNamingTheEngine();

        void ItShouldBeDegradedNamingTheEngine() => report.Signals.ShouldContain(signal =>
            signal.Name == "Search: SearXNG"
            && signal.State == StatusSignalState.Degraded
            && signal.Detail == "github is not loaded; is its API key set?");
    }

    [Test]
    public async Task SomeoneSignedOutRequestsTheStatus()
    {
        HttpResponseMessage response = await _api.Client(objectId: null).GetAsync("/api/status");

        ItShouldBeRefused();

        void ItShouldBeRefused() => response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
