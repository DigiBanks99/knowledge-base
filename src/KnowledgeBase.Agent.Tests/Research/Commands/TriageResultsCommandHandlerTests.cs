using System.Collections.Concurrent;

using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Curation;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Status;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class TriageResultsCommandHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Searched = new(2026, 10, 27, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Atlas = "projects/atlas";
    private const string Release = "https://example.com/atlas-2";
    private const string Rant = "https://example.com/rant";

    private readonly ResearchTopics _state = new();
    private readonly IResearchStore _store;
    private readonly IResearchNoteWriter _noteWriter = Substitute.For<IResearchNoteWriter>();
    private readonly IModelServerHealth _modelServer = Substitute.For<IModelServerHealth>();
    private readonly IPromptTokenCounter _tokenCounter = Substitute.For<IPromptTokenCounter>();
    private readonly ConcurrentDictionary<string, AgentRun> _runs = new();
    private readonly List<ResearchNote> _notes = [];
    private readonly ScriptedAgent _scout = new(KnownAgents.Scout);
    private readonly ModelGate _gate;
    private readonly AgentRunTracker _runTracker;
    private readonly ScoutTools _tools;
    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly TriageResultsCommandHandler _handler;

    public TriageResultsCommandHandlerTests()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Discovered) with { Brief = "News about the Atlas framework.", Queries = ["atlas release"] };
        _store = FakeResearchStore.Holding(_state);
        Add(Atlas, Release, Rant);

        TimeProvider timeProvider = _timeProvider;
        timeProvider.GetUtcNow().Returns(Now);
        _modelServer.IsAvailableToAsync(KnownAgents.Scout, Arg.Any<CancellationToken>()).Returns(true);
        _tokenCounter.CountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success<int, Error>(500));
        _noteWriter.WriteAsync(Arg.Do<ResearchNote>(_notes.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        IAgentRunStore runStore = Substitute.For<IAgentRunStore>();
        runStore
            .SaveAsync(Arg.Do<AgentRun>(run => _runs[run.Id] = run), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _runTracker = new AgentRunTracker(NullLogger<AgentRunTracker>.Instance, runStore, [], timeProvider);
        _gate = new ModelGate(Options.Create(new ResearchSettings()));
        _tools = new ScoutTools(_runTracker, _store, timeProvider);
        _handler = BuildHandler(new ResearchSettings { ScoutRunsPerCycle = 5, ScoutPromptTokenBudget = 1000 });
    }

    public void Dispose() => _gate.Dispose();

    [Test]
    public async Task TheScoutKeepsOneResultAndDiscardsTheOther()
    {
        _scout.Behaviour = async (prompt, token) =>
        {
            await (prompt.Contains(Release)
                ? CallToolAsync(nameof(ScoutTools.KeepResult), () => _tools.KeepResult("Atlas 2 is released.", ["Atlas 2 is out."], token))
                : CallToolAsync(nameof(ScoutTools.DiscardResult), () => _tools.DiscardResult("A rant.", token)));
            return "Judged.";
        };

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldJudgeBothResults();
        ItShouldWriteOneNoteWithTheKeptResultOnly();
        ItShouldMoveTheBatchToAwaiting();
        ItShouldRecordOneTriageRunPerResult();
        ItShouldGiveTheScoutTheBriefBeforeTheContent();

        void ItShouldJudgeBothResults() => result.Value.Judged.ShouldBe(2);

        void ItShouldWriteOneNoteWithTheKeptResultOnly()
        {
            ResearchNote note = _notes.ShouldHaveSingleItem();
            note.FileName.ShouldBe("research-projects--atlas-20261027-070000.md");
            note.Content.ReplaceLineEndings("\n").ShouldStartWith("---\norigin: research\ntopic: projects/atlas\nsources:\n  - https://example.com/atlas-2\n---");
            note.Content.ShouldContain("Atlas 2 is released.");
            note.Content.ShouldContain("> Atlas 2 is out.");
            note.Content.ShouldNotContain("rant");
            note.Content.ShouldNotContain("Page text");
        }

        void ItShouldMoveTheBatchToAwaiting()
        {
            result.Value.Awaiting.ShouldBe([Atlas]);
            _state.PendingBatches.ShouldBeEmpty();
            _state.AwaitingBatches[Atlas].ShouldSatisfyAllConditions(
                awaiting => awaiting.NoteFile.ShouldBe("research-projects--atlas-20261027-070000.md"),
                awaiting => awaiting.NotedAt.ShouldBe(Now),
                awaiting => awaiting.Batch.Results.ShouldAllBe(judged => judged.Verdict != null));
        }

        void ItShouldRecordOneTriageRunPerResult() => _runs.Values.ShouldSatisfyAllConditions(
            runs => runs.Count.ShouldBe(2),
            runs => runs.ShouldAllBe(run => run.Agent == KnownAgents.Scout && run.Status == AgentRunStatus.Succeeded && run.Topic == Atlas),
            runs => runs.Select(run => run.Trigger).ShouldBe(
                [new AgentRunTrigger(AgentRunTriggerKind.Triage, Release), new AgentRunTrigger(AgentRunTriggerKind.Triage, Rant)],
                ignoreOrder: true));

        void ItShouldGiveTheScoutTheBriefBeforeTheContent() => _scout.Prompts.ShouldAllBe(prompt =>
            prompt.IndexOf("News about the Atlas framework.", StringComparison.Ordinal) < prompt.IndexOf("<content>", StringComparison.Ordinal)
            && prompt.Contains("Page text of"));
    }

    [Test]
    public async Task TheScoutDiscardsEveryResult()
    {
        _scout.Behaviour = async (_, token) =>
        {
            await CallToolAsync(nameof(ScoutTools.DiscardResult), () => _tools.DiscardResult("Noise.", token));
            return "Judged.";
        };

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldCompleteTheBatchAsEmpty();
        ItShouldWriteNoNote();
        ItShouldBackTheTopicOff();

        void ItShouldCompleteTheBatchAsEmpty()
        {
            result.Value.Completed.ShouldBe([Atlas]);
            _state.PendingBatches.ShouldBeEmpty();
            _state.AwaitingBatches.ShouldBeEmpty();
            _state.CompletedBatches.ShouldHaveSingleItem().Results.ShouldAllBe(judged => judged.Verdict != null && !judged.Verdict.Kept);
        }

        void ItShouldWriteNoNote() => _notes.ShouldBeEmpty();

        void ItShouldBackTheTopicOff() => _state.Topics[Atlas].Interval.ShouldBe(TimeSpan.FromHours(48));
    }

    [Test]
    public async Task TheScoutEndsWithoutAVerdict()
    {
        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldRecordEveryRunAsFailed();
        ItShouldCountEveryResultAsDiscardedWithTheFailure();
        ItShouldCompleteTheBatchAsEmpty();

        void ItShouldRecordEveryRunAsFailed() => _runs.Values.ShouldAllBe(run => run.Status == AgentRunStatus.Failed && run.Error!.StartsWith("The Scout ended without a verdict"));

        void ItShouldCountEveryResultAsDiscardedWithTheFailure() => _state.CompletedBatches.Single().Results.ShouldAllBe(
            judged => !judged.Verdict!.Kept && judged.Verdict.Failure!.Contains("The Scout ended without a verdict"));

        void ItShouldCompleteTheBatchAsEmpty() => result.Value.Completed.ShouldBe([Atlas]);
    }

    [Test]
    public async Task ARestartFindsHalfTheResultsJudged()
    {
        _state.PendingBatches[Atlas] = _state.PendingBatches[Atlas] with
        {
            Results =
            [
                _state.PendingBatches[Atlas].Results[0] with { Verdict = ScoutVerdict.Keep("Atlas 2 is released.", [], Searched) },
                _state.PendingBatches[Atlas].Results[1]
            ]
        };
        _scout.Behaviour = async (_, token) =>
        {
            await CallToolAsync(nameof(ScoutTools.DiscardResult), () => _tools.DiscardResult("A rant.", token));
            return "Judged.";
        };

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldJudgeOnlyTheResultWithoutAVerdict();
        ItShouldKeepTheEarlierVerdict();

        void ItShouldJudgeOnlyTheResultWithoutAVerdict()
        {
            _scout.Prompts.ShouldHaveSingleItem().ShouldContain(Rant);
            result.Value.Judged.ShouldBe(1);
        }

        void ItShouldKeepTheEarlierVerdict() => _notes.ShouldHaveSingleItem().Content.ShouldContain("Atlas 2 is released.");
    }

    [Test]
    public async Task ARestartFindsTheNoteAlreadyWritten()
    {
        _scout.Behaviour = async (_, token) =>
        {
            await CallToolAsync(nameof(ScoutTools.KeepResult), () => _tools.KeepResult("Useful.", [], token));
            return "Judged.";
        };
        _noteWriter.Exists(Arg.Any<ResearchNote>()).Returns(true);

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldNotWriteItAgain();
        ItShouldMoveTheBatchToAwaiting();

        void ItShouldNotWriteItAgain() => _notes.ShouldBeEmpty();

        void ItShouldMoveTheBatchToAwaiting() => result.Value.Awaiting.ShouldBe([Atlas]);
    }

    [Test]
    public async Task AResultsPageCouldNotBeFetched()
    {
        _state.PendingBatches[Atlas] = _state.PendingBatches[Atlas] with
        {
            Results = [_state.PendingBatches[Atlas].Results[0] with { Page = FetchedPage.Failed(Searched, "Refused: private address.") }]
        };

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldNotRunTheScout();
        ItShouldDiscardTheResultWithTheFetchError();

        void ItShouldNotRunTheScout() => _scout.Prompts.ShouldBeEmpty();

        void ItShouldDiscardTheResultWithTheFetchError()
        {
            result.Value.Completed.ShouldBe([Atlas]);
            _state.CompletedBatches.Single().Results.Single().Verdict!.Reason.ShouldBe("The page could not be fetched: Refused: private address.");
        }
    }

    [Test]
    public async Task APageIsStillBeingFetched()
    {
        _state.PendingBatches[Atlas] = _state.PendingBatches[Atlas] with
        {
            Results = [.. _state.PendingBatches[Atlas].Results, _state.PendingBatches[Atlas].Results[0] with { Url = "https://example.com/slow", Page = null }]
        };

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldWaitForTheWholeBatch();

        void ItShouldWaitForTheWholeBatch()
        {
            _scout.Prompts.ShouldBeEmpty();
            result.Value.Judged.ShouldBe(0);
            _state.PendingBatches.Keys.ShouldBe([Atlas]);
        }
    }

    [Test]
    public async Task MoreResultsAreWaitingThanACycleJudges()
    {
        TriageResultsCommandHandler handler = BuildHandler(new ResearchSettings { ScoutRunsPerCycle = 1, ScoutPromptTokenBudget = 1000 });
        _scout.Behaviour = async (_, token) =>
        {
            await CallToolAsync(nameof(ScoutTools.KeepResult), () => _tools.KeepResult("Useful.", [], token));
            return "Judged.";
        };

        Result<TriageCycle, Error> result = await handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldJudgeUpToTheCap();
        ItShouldLeaveTheBatchPending();

        void ItShouldJudgeUpToTheCap() => result.Value.Judged.ShouldBe(1);

        void ItShouldLeaveTheBatchPending()
        {
            _state.PendingBatches[Atlas].Results.Count(judged => judged.Verdict is null).ShouldBe(1);
            _notes.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task ThePromptIsOverBudget()
    {
        _tokenCounter.CountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success<int, Error>(1001));

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldNotRunTheScout();
        ItShouldSayHowFarOverItIs();

        void ItShouldNotRunTheScout() => _scout.Prompts.ShouldBeEmpty();

        void ItShouldSayHowFarOverItIs() => _state.CompletedBatches.Single().Results.ShouldAllBe(
            judged => judged.Verdict!.Failure!.StartsWith("The Scout prompt is 1001 tokens, over the budget of 1000."));
    }

    [Test]
    public async Task TheModelTimesOut()
    {
        _scout.Behaviour = (_, _) => throw new TaskCanceledException("The operation was cancelled because it exceeded the timeout.");

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldLeaveTheResultsUnjudged();

        void ItShouldLeaveTheResultsUnjudged()
        {
            result.Value.Judged.ShouldBe(0);
            _state.PendingBatches[Atlas].Results.ShouldAllBe(judged => judged.Verdict == null);
        }
    }

    [Test]
    public async Task TheModelServerIsUnhealthy()
    {
        _modelServer.IsAvailableToAsync(KnownAgents.Scout, Arg.Any<CancellationToken>()).Returns(false);

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldSkipTheCycle();
        ItShouldNotRunTheScout();

        void ItShouldSkipTheCycle() => result.Value.Skipped.ShouldBeTrue();

        void ItShouldNotRunTheScout() => _scout.Prompts.ShouldBeEmpty();
    }

    [Test]
    public async Task TheScoutIsDisabled()
    {
        TriageResultsCommandHandler handler = BuildHandler(new ResearchSettings { ScoutEnabled = false });

        Result<TriageCycle, Error> result = await handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldSkipTheCycle();
        ItShouldLeaveTheBatchPending();

        void ItShouldSkipTheCycle()
        {
            result.Value.Skipped.ShouldBeTrue();
            _scout.Prompts.ShouldBeEmpty();
        }

        void ItShouldLeaveTheBatchPending() => _state.PendingBatches.Keys.ShouldBe([Atlas]);
    }

    private TriageResultsCommandHandler BuildHandler(ResearchSettings settings) =>
        new(
            NullLogger<TriageResultsCommandHandler>.Instance,
            Options.Create(settings),
            _store,
            _noteWriter,
            _modelServer,
            _tokenCounter,
            _gate,
            _runTracker,
            _scout,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [$"{KnownAgents.Scout}:Instructions"] = "Keep or discard each result." })
                .Build(),
            _timeProvider);

    private void Add(string topic, params string[] urls) =>
        _state.PendingBatches[topic] = new ResearchBatch(
            topic,
            Searched,
            [
                .. urls.Select(url => new BatchResult(
                    "a query",
                    url,
                    new SearchResult(url, $"Title of {url}", null, "SearXNG", null),
                    FetchedPage.Content(Searched, $"Page text of {url}", false)))
            ]);

    /// <summary>Calls a tool the way the agent's tool middleware does: recorded on the run, its error passed back to the model.</summary>
    private async Task CallToolAsync(string tool, Func<Task<string>> call)
    {
        try
        {
            await call();
            await _runTracker.RecordToolCallAsync(tool, null, null);
        }
        catch (InvalidOperationException ex)
        {
            await _runTracker.RecordToolCallAsync(tool, null, ex.Message);
        }
    }
}
