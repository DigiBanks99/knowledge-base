using System.Net;

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
    public async Task SomeoneSignedOutRequestsTheStatus()
    {
        HttpResponseMessage response = await _api.Client(objectId: null).GetAsync("/api/status");

        ItShouldBeRefused();

        void ItShouldBeRefused() => response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}