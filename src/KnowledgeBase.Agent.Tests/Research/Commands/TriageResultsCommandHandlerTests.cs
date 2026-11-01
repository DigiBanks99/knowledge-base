using System.Collections.Concurrent;

using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Curation;
using KnowledgeBase.Agent.Research.Fetch;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Status;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class TriageResultsCommandHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Searched = new(2026, 10, 27, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Atlas = "projects/atlas";
    private const string Brief = "News about the Atlas framework.";
    private const string Release = "https://example.com/atlas-2";
    private const string Rant = "https://example.com/rant";
    private const string NoteFile = "research-projects--atlas-20261027-070000.md";

    private readonly ResearchTopics _state = new();
    private readonly IResearchStore _store;
    private readonly IResearchNoteWriter _noteWriter = Substitute.For<IResearchNoteWriter>();
    private readonly IModelServerHealth _modelServer = Substitute.For<IModelServerHealth>();
    private readonly IPromptTokenCounter _tokenCounter = Substitute.For<IPromptTokenCounter>();
    private readonly ISearchRunLog _searchRunLog = Substitute.For<ISearchRunLog>();
    private readonly IKeeper _keeper = Substitute.For<IKeeper>();
    private readonly ConcurrentDictionary<string, AgentRun> _runs = new();
    private readonly List<ResearchNote> _notes = [];
    private readonly ModelGate _gate;
    private readonly AgentRunTracker _runTracker;
    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly TriageResultsCommandHandler _handler;

    public TriageResultsCommandHandlerTests()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Discovered) with { Brief = Brief, Queries = ["atlas release"] };
        _store = FakeResearchStore.Holding(_state);
        Add(Atlas, Release, Rant);

        TimeProvider timeProvider = _timeProvider;
        timeProvider.GetUtcNow().Returns(Now);
        _modelServer.IsKeeperServerAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        _tokenCounter.CountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success<int, Error>(500));
        _noteWriter.WriteAsync(Arg.Do<ResearchNote>(_notes.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // The release is worth keeping, the rant is not.
        _keeper.ScreenAsync(Arg.Any<KeeperPage>(), Arg.Any<CancellationToken>())
            .Returns(call => Screened(call.Arg<KeeperPage>().Url == Release ? 0.8 : 0.3, 0.1));
        _keeper.DescribeAsync(Arg.Any<KeeperPage>(), Arg.Any<CancellationToken>())
            .Returns(Described("release", 0.81, (1, 0.95), (2, 0.05)));

        IAgentRunStore runStore = Substitute.For<IAgentRunStore>();
        runStore
            .SaveAsync(Arg.Do<AgentRun>(run => _runs[run.Id] = run), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _runTracker = new AgentRunTracker(NullLogger<AgentRunTracker>.Instance, runStore, [], timeProvider);
        _gate = new ModelGate(Options.Create(new ResearchSettings()));
        _handler = BuildHandler(new ResearchSettings { KeeperCallsPerCycle = 10 });
    }

    public void Dispose() => _gate.Dispose();

    [Test]
    public async Task TheKeeperKeepsOneResultAndDiscardsTheOther()
    {
        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldJudgeBothResults();
        ItShouldGiveTheKeeperTheBriefAndTheNumberedParagraphs();
        ItShouldAskForKindAndQuotesOnlyForTheKeptResult();
        ItShouldStoreTheProbabilitiesKindAndQuotes();
        ItShouldDeriveTheDiscardsReason();
        ItShouldWriteOneNoteWithTheKeptResultOnly();
        ItShouldMoveTheBatchToAwaiting();
        ItShouldRecordEachCallAsARunWithItsProbabilities();

        void ItShouldJudgeBothResults() => result.Value.Judged.ShouldBe(2);

        void ItShouldGiveTheKeeperTheBriefAndTheNumberedParagraphs() => _keeper.Received(1).ScreenAsync(
            Arg.Is<KeeperPage>(page => page.Url == Release
                && page.Topic == Brief
                && page.Title == $"Title of {Release}"
                && page.Paragraphs.SequenceEqual(Paragraphs(Release))
                && page.QuoteOptions == 50),
            Arg.Any<CancellationToken>());

        void ItShouldAskForKindAndQuotesOnlyForTheKeptResult()
        {
            _keeper.Received(1).DescribeAsync(Arg.Is<KeeperPage>(page => page.Url == Release), Arg.Any<CancellationToken>());
            _keeper.DidNotReceive().DescribeAsync(Arg.Is<KeeperPage>(page => page.Url == Rant), Arg.Any<CancellationToken>());
        }

        void ItShouldStoreTheProbabilitiesKindAndQuotes() => VerdictOf(Release).ShouldSatisfyAllConditions(
            verdict => verdict.Kept.ShouldBeTrue(),
            verdict => verdict.Summary.ShouldBeNull(),
            verdict => verdict.Useful.ShouldBe(0.8),
            verdict => verdict.Injection.ShouldBe(0.1),
            verdict => verdict.Kind.ShouldBe("release"),
            verdict => verdict.KindProbability.ShouldBe(0.81),
            verdict => verdict.Quotes.ShouldBe([Paragraphs(Release)[0]]));

        void ItShouldDeriveTheDiscardsReason() => VerdictOf(Rant).ShouldSatisfyAllConditions(
            verdict => verdict.Kept.ShouldBeFalse(),
            verdict => verdict.Reason.ShouldBe("Not useful enough for the topic (useful 0.30, below 0.60)."),
            verdict => verdict.Useful.ShouldBe(0.3));

        void ItShouldWriteOneNoteWithTheKeptResultOnly()
        {
            ResearchNote note = _notes.ShouldHaveSingleItem();
            note.FileName.ShouldBe(NoteFile);
            note.Content.ReplaceLineEndings("\n").ShouldStartWith("---\norigin: research\ntopic: projects/atlas\nsources:\n  - https://example.com/atlas-2\n---");
            note.Content.ShouldContain("Kind: release");
            note.Content.ShouldContain($"> {Paragraphs(Release)[0]}");
            note.Content.ShouldNotContain(Paragraphs(Release)[1]);
            note.Content.ShouldNotContain(Rant);
        }

        void ItShouldMoveTheBatchToAwaiting()
        {
            result.Value.Awaiting.ShouldBe([Atlas]);
            _state.PendingBatches.ShouldBeEmpty();
            _state.AwaitingBatches[Atlas].NoteFile.ShouldBe(NoteFile);
        }

        void ItShouldRecordEachCallAsARunWithItsProbabilities() => _runs.Values.ShouldSatisfyAllConditions(
            runs => runs.ShouldAllBe(run => run.Agent == "Keeper"
                && run.Status == AgentRunStatus.Succeeded
                && run.Topic == Atlas
                && run.Trigger.Kind == AgentRunTriggerKind.Triage
                && run.ToolCalls.Count == 0),
            runs => runs.Select(run => (run.Trigger.Reference, run.Summary)).ShouldBe(
                [
                    (Release, "Useful 0.80, injection 0.10: kept."),
                    (Release, "Kind release (0.81); quotes 1."),
                    (Rant, "Useful 0.30, injection 0.10: discarded.")
                ],
                ignoreOrder: true));
    }

    [Test]
    public async Task AResultLooksLikeAnInjection()
    {
        _keeper.ScreenAsync(Arg.Any<KeeperPage>(), Arg.Any<CancellationToken>()).Returns(Screened(0.95, 0.92));

        await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldDiscardItWhateverItsUsefulness();
        ItShouldNotAskTheSecondCall();

        void ItShouldDiscardItWhateverItsUsefulness() => _state.CompletedBatches.Single().Results.ShouldAllBe(judged =>
            !judged.Verdict!.Kept && judged.Verdict.Reason == "Looks like instructions aimed at an AI (injection 0.92, at or above 0.90).");

        void ItShouldNotAskTheSecondCall() => _keeper.DidNotReceive().DescribeAsync(Arg.Any<KeeperPage>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TheKeeperDiscardsEveryResult()
    {
        _keeper.ScreenAsync(Arg.Any<KeeperPage>(), Arg.Any<CancellationToken>()).Returns(Screened(0.2, 0.1));

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldCompleteTheBatchAsEmpty();
        ItShouldWriteNoNote();
        ItShouldBackTheTopicOff();

        void ItShouldCompleteTheBatchAsEmpty()
        {
            result.Value.Completed.ShouldBe([Atlas]);
            _state.PendingBatches.ShouldBeEmpty();
            _state.CompletedBatches.ShouldHaveSingleItem().Results.ShouldAllBe(judged => judged.Verdict != null && !judged.Verdict.Kept);
        }

        void ItShouldWriteNoNote() => _notes.ShouldBeEmpty();

        void ItShouldBackTheTopicOff() => _state.Topics[Atlas].Interval.ShouldBe(TimeSpan.FromHours(48));
    }

    [Test]
    public async Task ABatchFromARecordedSearchIsTriaged()
    {
        _state.PendingBatches[Atlas] = _state.PendingBatches[Atlas] with { SearchRunId = "search-run" };
        List<AgentToolCall> appended = [];
        _searchRunLog
            .AppendAsync("search-run", Arg.Do<IReadOnlyList<AgentToolCall>>(appended.AddRange), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldAddEachPageAgainInCaseOneMissedItsLine();
        ItShouldAddEachVerdictToTheSearchRun();
        ItShouldAddTheNoteToTheSearchRun();

        void ItShouldAddEachPageAgainInCaseOneMissedItsLine() => appended
            .Where(line => line.Tool == SearchRuns.FetchStep)
            .Select(line => line.Target)
            .ShouldBe([Release, Rant]);

        void ItShouldAddEachVerdictToTheSearchRun() => appended
            .Where(line => line.Tool is SearchRuns.KeepStep or SearchRuns.DiscardStep)
            .Select(line => (line.Tool, line.Target, line.Succeeded, line.Detail))
            .ShouldBe(
            [
                (SearchRuns.KeepStep, Release, true, null),
                (SearchRuns.DiscardStep, Rant, true, "Not useful enough for the topic (useful 0.30, below 0.60).")
            ]);

        void ItShouldAddTheNoteToTheSearchRun() => appended
            .Where(line => line.Tool == SearchRuns.NoteStep)
            .ShouldHaveSingleItem()
            .Target.ShouldBe(NoteFile);
    }

    [Test]
    public async Task TheKeeperCannotAnswer()
    {
        _keeper.ScreenAsync(Arg.Any<KeeperPage>(), Arg.Any<CancellationToken>()).Returns(Failure<Screening>("timed out."));

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldLeaveTheResultsWaiting();
        ItShouldCountTheAttempt();
        ItShouldRecordTheCallsAsFailedRuns();

        void ItShouldLeaveTheResultsWaiting()
        {
            result.Value.Judged.ShouldBe(0);
            _state.PendingBatches[Atlas].Results.ShouldAllBe(judged => judged.Verdict == null);
        }

        void ItShouldCountTheAttempt() => _state.PendingBatches[Atlas].Results.ShouldAllBe(judged => judged.Attempts == 1);

        void ItShouldRecordTheCallsAsFailedRuns() => _runs.Values.ShouldSatisfyAllConditions(
            runs => runs.Count.ShouldBe(2),
            runs => runs.ShouldAllBe(run => run.Status == AgentRunStatus.Failed && run.Error!.Contains("timed out.")));
    }

    [Test]
    public async Task TheKeeperFailsForTheLastTime()
    {
        _state.PendingBatches[Atlas] = _state.PendingBatches[Atlas] with
        {
            Results = [.. _state.PendingBatches[Atlas].Results.Select(waiting => waiting with { Attempts = 2 })]
        };
        _keeper.ScreenAsync(Arg.Any<KeeperPage>(), Arg.Any<CancellationToken>()).Returns(Failure<Screening>("timed out."));

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldGiveUpWithAFailedVerdict();
        ItShouldLetTheBatchFinish();

        void ItShouldGiveUpWithAFailedVerdict() => _state.CompletedBatches.Single().Results.ShouldAllBe(judged =>
            !judged.Verdict!.Kept && judged.Verdict.Failure == "The Keeper failed 3 times: The Keeper could not answer: timed out.");

        void ItShouldLetTheBatchFinish() => result.Value.Completed.ShouldBe([Atlas]);
    }

    [Test]
    public async Task TheSecondCallFails()
    {
        _keeper.DescribeAsync(Arg.Any<KeeperPage>(), Arg.Any<CancellationToken>()).Returns(Failure<Description>("timed out."));

        await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldLeaveTheKeptResultWaiting();

        void ItShouldLeaveTheKeptResultWaiting() => _state.PendingBatches[Atlas].Results.Single(judged => judged.Url == Release)
            .ShouldSatisfyAllConditions(
                waiting => waiting.Verdict.ShouldBeNull(),
                waiting => waiting.Attempts.ShouldBe(1));
    }

    [Test]
    public async Task TheSecondCallFailsForTheLastTime()
    {
        _state.PendingBatches[Atlas] = _state.PendingBatches[Atlas] with
        {
            Results = [.. _state.PendingBatches[Atlas].Results.Select(waiting => waiting with { Attempts = 2 })]
        };
        _keeper.DescribeAsync(Arg.Any<KeeperPage>(), Arg.Any<CancellationToken>()).Returns(Failure<Description>("timed out."));

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldKeepTheUsefulResultWithoutKindOrQuotes();
        ItShouldWriteItsNote();

        void ItShouldKeepTheUsefulResultWithoutKindOrQuotes() => VerdictOf(Release).ShouldSatisfyAllConditions(
            verdict => verdict.Kept.ShouldBeTrue(),
            verdict => verdict.Useful.ShouldBe(0.8),
            verdict => verdict.Kind.ShouldBeNull(),
            verdict => verdict.Quotes.ShouldBeEmpty());

        void ItShouldWriteItsNote() => result.Value.Awaiting.ShouldBe([Atlas]);
    }

    [Test]
    public async Task ARestartFindsHalfTheResultsJudged()
    {
        _state.PendingBatches[Atlas] = _state.PendingBatches[Atlas] with
        {
            Results =
            [
                _state.PendingBatches[Atlas].Results[0] with { Verdict = Verdict.Keep(null, ["Atlas 2 is out."], Searched) },
                _state.PendingBatches[Atlas].Results[1]
            ]
        };

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldJudgeOnlyTheResultWithoutAVerdict();
        ItShouldKeepTheEarlierVerdict();

        void ItShouldJudgeOnlyTheResultWithoutAVerdict()
        {
            _keeper.Received(1).ScreenAsync(Arg.Any<KeeperPage>(), Arg.Any<CancellationToken>());
            _keeper.Received(1).ScreenAsync(Arg.Is<KeeperPage>(page => page.Url == Rant), Arg.Any<CancellationToken>());
            result.Value.Judged.ShouldBe(1);
        }

        void ItShouldKeepTheEarlierVerdict() => _notes.ShouldHaveSingleItem().Content.ShouldContain("> Atlas 2 is out.");
    }

    [Test]
    public async Task ARestartFindsTheNoteAlreadyWritten()
    {
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

        ItShouldNotCallTheKeeper();
        ItShouldDiscardTheResultWithTheFetchError();

        void ItShouldNotCallTheKeeper() => _keeper.ReceivedCalls().ShouldBeEmpty();

        void ItShouldDiscardTheResultWithTheFetchError()
        {
            result.Value.Completed.ShouldBe([Atlas]);
            _state.CompletedBatches.Single().Results.Single().Verdict!.Reason.ShouldBe("The page could not be fetched: Refused: private address.");
        }
    }

    [Test]
    public async Task APageHasNoProse()
    {
        _state.PendingBatches[Atlas] = _state.PendingBatches[Atlas] with
        {
            Results = [_state.PendingBatches[Atlas].Results[0] with { Page = FetchedPage.Content(Searched, "# Atlas\n\n![logo](https://example.com/logo.png)", false) }]
        };

        await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldNotCallTheKeeper();
        ItShouldDiscardTheResult();

        void ItShouldNotCallTheKeeper() => _keeper.ReceivedCalls().ShouldBeEmpty();

        void ItShouldDiscardTheResult() =>
            _state.CompletedBatches.Single().Results.Single().Verdict!.Reason.ShouldBe("The page has no prose paragraphs to judge.");
    }

    [Test]
    public async Task APageIsLongerThanTheKeeperReads()
    {
        // A token a character, so the budget fits the first paragraph and not the second.
        _tokenCounter.CountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<int, Error>(call.Arg<string>().Length));
        TriageResultsCommandHandler handler = BuildHandler(new ResearchSettings { KeeperContentTokenBudget = Paragraphs(Release)[0].Length + 10 });

        await handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldSendOnlyTheParagraphsThatFit();

        void ItShouldSendOnlyTheParagraphsThatFit() => _keeper.Received(1).ScreenAsync(
            Arg.Is<KeeperPage>(page => page.Url == Release && page.Paragraphs.SequenceEqual(new[] { Paragraphs(Release)[0] })),
            Arg.Any<CancellationToken>());
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
            _keeper.ReceivedCalls().ShouldBeEmpty();
            result.Value.Judged.ShouldBe(0);
            _state.PendingBatches.Keys.ShouldBe([Atlas]);
        }
    }

    [Test]
    public async Task MoreResultsAreWaitingThanACycleJudges()
    {
        TriageResultsCommandHandler handler = BuildHandler(new ResearchSettings { KeeperCallsPerCycle = 1 });

        Result<TriageCycle, Error> result = await handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldFinishTheKeptResultsSecondCallThenStop();
        ItShouldLeaveTheBatchPending();

        void ItShouldFinishTheKeptResultsSecondCallThenStop()
        {
            result.Value.Judged.ShouldBe(1);
            _runs.Count.ShouldBe(2);
        }

        void ItShouldLeaveTheBatchPending()
        {
            _state.PendingBatches[Atlas].Results.Single(judged => judged.Verdict is null).Url.ShouldBe(Rant);
            _notes.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task TheModelServerIsUnhealthy()
    {
        _modelServer.IsKeeperServerAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);

        Result<TriageCycle, Error> result = await _handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldSkipTheCycle();
        ItShouldNotCallTheKeeper();

        void ItShouldSkipTheCycle() => result.Value.Skipped.ShouldBeTrue();

        void ItShouldNotCallTheKeeper() => _keeper.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task TriageIsDisabled(bool triageEnabled, bool scoutEnabled)
    {
        TriageResultsCommandHandler handler = BuildHandler(new ResearchSettings { TriageEnabled = triageEnabled, ScoutEnabled = scoutEnabled });

        Result<TriageCycle, Error> result = await handler.Handle(new TriageResultsCommand(), CancellationToken.None);

        ItShouldSkipTheCycle();
        ItShouldLeaveTheBatchPending();

        void ItShouldSkipTheCycle()
        {
            result.Value.Skipped.ShouldBeTrue();
            _keeper.ReceivedCalls().ShouldBeEmpty();
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
            new ContentBudget(_tokenCounter),
            _gate,
            _runTracker,
            _searchRunLog,
            _keeper,
            _timeProvider);

    private Verdict VerdictOf(string url) =>
        (_state.AwaitingBatches.GetValueOrDefault(Atlas)?.Batch ?? _state.PendingBatches[Atlas])
            .Results.Single(judged => judged.Url == url).Verdict!;

    private static string[] Paragraphs(string url) =>
        [$"The first paragraph of {url}, which says what is new.", $"The second paragraph of {url}, which says why it matters."];

    private static Result<Screening, Error> Screened(double useful, double injection) => new Screening(useful, injection);

    private static Result<Description, Error> Described(string kind, double probability, params (int Number, double Probability)[] quotes) =>
        new Description(kind, probability, quotes.ToDictionary(quote => quote.Number, quote => quote.Probability));

    private static Result<T, Error> Failure<T>(string reason) => new KeeperCallFailedError(reason);

    private void Add(string topic, params string[] urls) =>
        _state.PendingBatches[topic] = new ResearchBatch(
            topic,
            Searched,
            [
                .. urls.Select(url => new BatchResult(
                    "a query",
                    url,
                    new SearchResult(url, $"Title of {url}", null, "SearXNG", null),
                    FetchedPage.Content(Searched, $"# Heading\n\n{string.Join("\n\n", Paragraphs(url))}", false)))
            ]);
}
