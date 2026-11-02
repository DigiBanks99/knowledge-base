using System.Net;

using KnowledgeBase.Agent.Diagnostics;

using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

public sealed class LogsApiTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly TestApi _api = new();
    private readonly string _marker = $"marker-{Guid.NewGuid():N}";

    public void Dispose() => _api.Dispose();

    // The test host clears its logging providers so the console stays quiet, which also removes the one that feeds the
    // buffer; so the buffer is written to directly here. The marker is only in a property value, as a topic would be.
    private void SeedWarning() =>
        _api.Get<LogBuffer>().Add(
            At,
            LogLevel.Warning,
            "Research",
            "KnowledgeBase.Agent.Research.Search.TopicSearch",
            "A search source failed.",
            new Dictionary<string, string> { ["Topic"] = _marker },
            null);

    [Test]
    public async Task ASearchForAWarningIsFound()
    {
        SeedWarning();

        HttpResponseMessage response = await _api.Client().GetAsync($"/api/logs?search={_marker}");
        LogEntry[] entries = await TestApi.ReadAsync<LogEntry[]>(response);

        ItShouldSucceed();
        ItShouldReturnTheWarning();

        void ItShouldSucceed() => response.StatusCode.ShouldBe(HttpStatusCode.OK);

        void ItShouldReturnTheWarning() => entries.Select(entry => entry.Message).ShouldBe(["A search source failed."]);
    }

    [Test]
    public async Task AnotherAreaIsAsked()
    {
        SeedWarning();

        HttpResponseMessage response = await _api.Client().GetAsync($"/api/logs?area=Review&search={_marker}");
        LogEntry[] entries = await TestApi.ReadAsync<LogEntry[]>(response);

        ItShouldReturnNothing(entries);

        static void ItShouldReturnNothing(LogEntry[] matched) => matched.ShouldBeEmpty();
    }

    [Test]
    public async Task AWarningIsLeftOutWhenOnlyErrorsAreAsked()
    {
        SeedWarning();

        HttpResponseMessage response = await _api.Client().GetAsync($"/api/logs?level=Error&search={_marker}");
        LogEntry[] entries = await TestApi.ReadAsync<LogEntry[]>(response);

        ItShouldReturnNothing(entries);

        static void ItShouldReturnNothing(LogEntry[] matched) => matched.ShouldBeEmpty();
    }

    [Test]
    public async Task SomeoneSignedOutRequestsTheLogs()
    {
        HttpResponseMessage response = await _api.Client(objectId: null).GetAsync("/api/logs");

        ItShouldBeRefused();

        void ItShouldBeRefused() => response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    }
}
