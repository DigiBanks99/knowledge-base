using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Search;

public sealed class SearchRunLogTests : IDisposable
{
    private const string RunId = "20261028-120000-search-0123456789abcdef";

    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly IAgentRunStore _store = Substitute.For<IAgentRunStore>();
    private readonly Dictionary<string, AgentRun> _saved = [];
    private readonly SearchRunLog _log;

    public SearchRunLogTests()
    {
        _store.SaveAsync(Arg.Do<AgentRun>(run => _saved[run.Id] = run), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _store.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => _saved.GetValueOrDefault(call.Arg<string>()));
        _log = new SearchRunLog(NullLogger<SearchRunLog>.Instance, _store);
    }

    public void Dispose() => _log.Dispose();

    [Test]
    public async Task ASearchIsRecorded()
    {
        AgentRun run = Run(Line("Search", "atlas release"));

        await _log.RecordAsync(run, CancellationToken.None);

        ItShouldSaveTheRun();

        void ItShouldSaveTheRun() => _saved[RunId].ShouldBe(run);
    }

    [Test]
    public async Task WhatBecameOfTheResultsIsAdded()
    {
        _saved[RunId] = Run(Line("Search", "atlas release"));

        await _log.AppendAsync(RunId, [Line("Fetch", "https://example.com/a")], CancellationToken.None);

        ItShouldAddTheLinesAfterTheSearch();

        void ItShouldAddTheLinesAfterTheSearch() =>
            _saved[RunId].ToolCalls.Select(call => call.Tool).ShouldBe(["Search", "Fetch"]);
    }

    [Test]
    public async Task AStepIsAddedTwiceAfterARestart()
    {
        _saved[RunId] = Run(Line("Search", "atlas release"), Line("Keep", "https://example.com/a"));

        await _log.AppendAsync(RunId, [Line("Keep", "https://example.com/a"), Line("Note", "research-note.md")], CancellationToken.None);

        ItShouldOnlyAddWhatIsNew();

        void ItShouldOnlyAddWhatIsNew() =>
            _saved[RunId].ToolCalls.Select(call => call.Tool).ShouldBe(["Search", "Keep", "Note"]);
    }

    [Test]
    public async Task ASearchEndsAfterAPageWasFetched()
    {
        _saved[RunId] = Run(Line("Search", "atlas release"), Line("Fetch", "https://example.com/a"));

        await _log.RecordAsync(Run(Line("Search", "atlas release")) with { Summary = "Done." }, CancellationToken.None);

        ItShouldKeepTheFetchedPage();

        void ItShouldKeepTheFetchedPage() => _saved[RunId].ShouldSatisfyAllConditions(
            run => run.Summary.ShouldBe("Done."),
            run => run.ToolCalls.Select(call => call.Tool).ShouldBe(["Search", "Fetch"]));
    }

    [Test]
    public async Task TheSearchRunNoLongerExists()
    {
        await _log.AppendAsync(RunId, [Line("Fetch", "https://example.com/a")], CancellationToken.None);

        ItShouldSaveNothing();

        void ItShouldSaveNothing() => _saved.ShouldBeEmpty();
    }

    [Test]
    public async Task TheRunStoreCannotBeWritten()
    {
        _store.SaveAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()).Returns(_ => throw new IOException("The disk is full."));
        _store.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(Run());

        Exception? recording = await Catch.ExceptionAsync(() => _log.RecordAsync(Run(), CancellationToken.None));
        Exception? appending = await Catch.ExceptionAsync(() => _log.AppendAsync(RunId, [Line("Fetch", "https://example.com/a")], CancellationToken.None));

        ItShouldNeverThrow();

        void ItShouldNeverThrow() => this.ShouldSatisfyAllConditions(
            () => recording.ShouldBeNull(),
            () => appending.ShouldBeNull());
    }

    private static AgentRun Run(params AgentToolCall[] lines) => new(
        RunId,
        SearchRuns.Agent,
        new AgentRunTrigger(AgentRunTriggerKind.Search, "Scheduled"),
        AgentRunStatus.Succeeded,
        Now,
        Now,
        "Found 1 result.",
        null,
        lines,
        FactOrigin.Research,
        "projects/atlas");

    private static AgentToolCall Line(string step, string target) => new(step, target, Now, true, null);
}
