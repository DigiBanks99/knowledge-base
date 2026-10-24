using System.Net;
using System.Net.Http.Json;

using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

public sealed class RunsApiTests : IDisposable
{
    private readonly TestApi _api = new();

    public void Dispose() => _api.Dispose();

    [Test]
    public async Task RunsAreRequested()
    {
        HttpClient client = _api.Client();
        AgentRunStore store = _api.Get<AgentRunStore>();
        await store.SaveAsync(Run("run-1", KnownAgents.FactFinder, minutes: 0), CancellationToken.None);
        await store.SaveAsync(Run("run-2", KnownAgents.ThreadWeaver, minutes: 1), CancellationToken.None);
        await store.SaveAsync(Run("run-3", KnownAgents.FactFinder, minutes: 2), CancellationToken.None);

        IReadOnlyList<AgentRun> all = await ListAsync(client, "/api/runs");
        IReadOnlyList<AgentRun> byAgent = await ListAsync(client, "/api/runs?agent=FactFinder");
        IReadOnlyList<AgentRun> byTrigger = await ListAsync(client, "/api/runs?triggerKind=Note&trigger=run-2.md");
        IReadOnlyList<AgentRun> atLeastOne = await ListAsync(client, "/api/runs?limit=0");

        ItShouldListTheNewestFirst();
        ItShouldFilterByAgent();
        ItShouldFilterByTrigger();
        ItShouldAlwaysReturnAtLeastOneRun();

        void ItShouldListTheNewestFirst() => all.Select(run => run.Id).ShouldBe(["run-3", "run-2", "run-1"]);

        void ItShouldFilterByAgent() => byAgent.Select(run => run.Id).ShouldBe(["run-3", "run-1"]);

        void ItShouldFilterByTrigger() => byTrigger.Select(run => run.Id).ShouldBe(["run-2"]);

        void ItShouldAlwaysReturnAtLeastOneRun() => atLeastOne.Select(run => run.Id).ShouldBe(["run-3"]);
    }

    [Test]
    [Arguments("run-404")]
    [Arguments("run.json")]
    public async Task AMissingRunIsRequested(string id)
    {
        HttpResponseMessage response = await _api.Client().GetAsync($"/api/runs/{id}");

        ItShouldNotFindIt();

        void ItShouldNotFindIt() => response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<IReadOnlyList<AgentRun>> ListAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<IReadOnlyList<AgentRun>>(url, TestApi.Json))!;

    private static AgentRun Run(string id, string agent, int minutes)
    {
        DateTimeOffset startedAt = DateTimeOffset.Parse("2026-10-24T10:00:00Z").AddMinutes(minutes);
        return new AgentRun(
            id,
            agent,
            new AgentRunTrigger(AgentRunTriggerKind.Note, $"{id}.md"),
            AgentRunStatus.Succeeded,
            startedAt,
            startedAt.AddSeconds(30),
            "Done.",
            null,
            []);
    }
}