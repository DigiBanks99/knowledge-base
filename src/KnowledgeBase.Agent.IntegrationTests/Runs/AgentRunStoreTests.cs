using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Runs;

public sealed class AgentRunStoreTests : IAsyncDisposable
{
    private readonly TestWiki _wiki = new();
    private readonly AgentRunStore _store;

    public AgentRunStoreTests()
    {
        _store = _wiki.Get<AgentRunStore>();
    }

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task RunsAreListed()
    {
        await _wiki.InitialiseAsync();
        AgentRun oldest = Run("run-1", KnownAgents.FactFinder, AgentRunTriggerKind.Note, "note-a.md", minutes: 0);
        AgentRun middle = Run("run-2", KnownAgents.ThreadWeaver, AgentRunTriggerKind.Facts, "facts-a.json", minutes: 1);
        AgentRun newest = Run("run-3", KnownAgents.FactFinder, AgentRunTriggerKind.Note, "note-b.md", minutes: 2);
        AgentRun[] savedOutOfOrder = [middle, newest, oldest];
        foreach (AgentRun run in savedOutOfOrder)
        {
            await _store.SaveAsync(run, CancellationToken.None);
        }

        IReadOnlyList<AgentRun> all = await List();
        IReadOnlyList<AgentRun> byAgent = await List(agent: "factfinder");
        IReadOnlyList<AgentRun> byTrigger = await List(triggerKind: AgentRunTriggerKind.Note, triggerReference: "NOTE-A.md");
        IReadOnlyList<AgentRun> limited = await List(limit: 2);

        ItShouldListTheNewestFirst();
        ItShouldFilterByAgentIgnoringCase();
        ItShouldFilterByTriggerIgnoringCase();
        ItShouldLimitTheNumberOfRuns();

        void ItShouldListTheNewestFirst() => Ids(all).ShouldBe(["run-3", "run-2", "run-1"]);

        void ItShouldFilterByAgentIgnoringCase() => Ids(byAgent).ShouldBe(["run-3", "run-1"]);

        void ItShouldFilterByTriggerIgnoringCase() => Ids(byTrigger).ShouldBe(["run-1"]);

        void ItShouldLimitTheNumberOfRuns() => Ids(limited).ShouldBe(["run-3", "run-2"]);
    }

    [Test]
    public async Task ARunIsReadBack()
    {
        await _wiki.InitialiseAsync();
        AgentRun saved = Run("run-1", KnownAgents.FactFinder, AgentRunTriggerKind.Note, "note-a.md", minutes: 0) with
        {
            ToolCalls = [new AgentToolCall("FactWriterTool", "feedback", DateTimeOffset.Parse("2026-10-24T10:00:30Z"), true, null)]
        };
        await _store.SaveAsync(saved, CancellationToken.None);

        AgentRun? read = await _store.GetAsync(saved.Id, CancellationToken.None);

        ItShouldKeepTheRun();
        ItShouldKeepTheToolCalls();

        void ItShouldKeepTheRun() => read.ShouldNotBeNull().ShouldSatisfyAllConditions(
            run => run.Agent.ShouldBe(saved.Agent),
            run => run.Trigger.ShouldBe(saved.Trigger),
            run => run.Status.ShouldBe(saved.Status),
            run => run.StartedAt.ShouldBe(saved.StartedAt));

        void ItShouldKeepTheToolCalls() => read!.ToolCalls.ShouldBe(saved.ToolCalls);
    }

    [Test]
    [Arguments("../wiki/index")]
    [Arguments("run-1.json")]
    [Arguments("")]
    public async Task AnUnsafeRunIdIsRequested(string id)
    {
        await _wiki.InitialiseAsync();

        AgentRun? read = await _store.GetAsync(id, CancellationToken.None);

        ItShouldFindNothing();

        void ItShouldFindNothing() => read.ShouldBeNull();
    }

    private Task<IReadOnlyList<AgentRun>> List(
        string? agent = null,
        AgentRunTriggerKind? triggerKind = null,
        string? triggerReference = null,
        int limit = 50) =>
        _store.ListAsync(agent, triggerKind, triggerReference, limit, CancellationToken.None);

    private static IReadOnlyList<string> Ids(IEnumerable<AgentRun> runs) => runs.Select(run => run.Id).ToList();

    private static AgentRun Run(string id, string agent, AgentRunTriggerKind kind, string reference, int minutes)
    {
        DateTimeOffset startedAt = DateTimeOffset.Parse("2026-10-24T10:00:00Z").AddMinutes(minutes);
        return new AgentRun(
            id,
            agent,
            new AgentRunTrigger(kind, reference),
            AgentRunStatus.Succeeded,
            startedAt,
            startedAt.AddSeconds(30),
            "Done.",
            null,
            []);
    }
}