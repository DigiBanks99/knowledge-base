using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Triage;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class ResolveAwaitingBatchesCommandHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Searched = new(2026, 10, 27, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private const string Atlas = "projects/atlas";
    private const string Note = "research-projects--atlas-20261027-070000.md";

    private readonly ResearchTopics _state = new();
    private readonly IResearchStore _store;
    private readonly IResearchNoteWriter _noteWriter = Substitute.For<IResearchNoteWriter>();
    private readonly IAgentRunStore _runStore = Substitute.For<IAgentRunStore>();
    private readonly List<AgentRun> _runs = [];
    private readonly ResolveAwaitingBatchesCommandHandler _handler;

    public ResolveAwaitingBatchesCommandHandlerTests()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Discovered) with
        {
            Queries = ["atlas release"],
            LastSearchedAt = Searched,
            Interval = TimeSpan.FromDays(4)
        };
        _state.AwaitingBatches[Atlas] = new AwaitingBatch(new ResearchBatch(Atlas, Searched, []), Note, Searched);
        _store = FakeResearchStore.Holding(_state);
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _runStore
            .ListAsync(new AgentRunFilter(KnownAgents.ThreadWeaver, AgentRunTriggerKind.Facts), Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<AgentRun>)_runs);
        _handler = new ResolveAwaitingBatchesCommandHandler(
            NullLogger<ResolveAwaitingBatchesCommandHandler>.Instance,
            _store,
            _noteWriter,
            _runStore,
            timeProvider);
    }

    [Test]
    public async Task TheThreadWeaverFiledSomethingFromTheNote()
    {
        _noteWriter.Locate(Note).Returns(NoteLocation.Processed);
        _runs.Add(Run(Note, "PatchFile"));

        Result<ResolvedBatches, Error> resolved = await _handler.Handle(new ResolveAwaitingBatchesCommand(), CancellationToken.None);

        ItShouldCheckTheTopicDailyAgain();
        ItShouldRecordWhenItPaidOff();
        ItShouldCompleteTheBatch();
        ItShouldReportThePositiveTopic();

        void ItShouldCheckTheTopicDailyAgain() => _state.Topics[Atlas].Interval.ShouldBe(TimeSpan.FromHours(24));
        void ItShouldRecordWhenItPaidOff() => _state.Topics[Atlas].LastPositiveAt.ShouldBe(Now);
        void ItShouldCompleteTheBatch() => _state.CompletedBatches.ShouldHaveSingleItem();
        void ItShouldReportThePositiveTopic() => resolved.Value.Positive.ShouldBe([Atlas]);
    }

    [Test]
    public async Task NothingWasFiledFromTheNote()
    {
        _noteWriter.Locate(Note).Returns(NoteLocation.Processed);

        Result<ResolvedBatches, Error> resolved = await _handler.Handle(new ResolveAwaitingBatchesCommand(), CancellationToken.None);

        ItShouldDoubleTheInterval();
        ItShouldCompleteTheBatch();
        ItShouldReportTheEmptyTopic();

        void ItShouldDoubleTheInterval() => _state.Topics[Atlas].Interval.ShouldBe(TimeSpan.FromDays(8));
        void ItShouldCompleteTheBatch() => _state.AwaitingBatches.ShouldBeEmpty();
        void ItShouldReportTheEmptyTopic() => resolved.Value.Empty.ShouldBe([Atlas]);
    }

    [Test]
    public async Task TheNoteFailedThreeTimesRunning()
    {
        _noteWriter.Locate(Note).Returns(NoteLocation.Failed);
        _state.Topics[Atlas] = _state.Topics[Atlas] with { ConsecutiveFailures = 2 };

        Result<ResolvedBatches, Error> resolved = await _handler.Handle(new ResolveAwaitingBatchesCommand(), CancellationToken.None);

        ItShouldLeaveTheIntervalAlone();
        ItShouldFlagTheTopicAsFailing();
        ItShouldCompleteTheBatchSoTheTopicCanBeSearchedAgain();
        ItShouldReportTheFailedTopic();

        void ItShouldLeaveTheIntervalAlone() => _state.Topics[Atlas].Interval.ShouldBe(TimeSpan.FromDays(4));
        void ItShouldFlagTheTopicAsFailing() => _state.Topics[Atlas].IsFailing.ShouldBeTrue();
        void ItShouldCompleteTheBatchSoTheTopicCanBeSearchedAgain() => _state.AwaitingBatches.ShouldBeEmpty();
        void ItShouldReportTheFailedTopic() => resolved.Value.Failed.ShouldBe([Atlas]);
    }

    [Test]
    public async Task TheNoteIsStillInThePipeline()
    {
        _noteWriter.Locate(Note).Returns(NoteLocation.Processing);

        await _handler.Handle(new ResolveAwaitingBatchesCommand(), CancellationToken.None);

        ItShouldKeepTheBatchAwaiting();
        ItShouldLeaveTheTopicAlone();
        ItShouldNotReadTheRunStore();

        void ItShouldKeepTheBatchAwaiting() => _state.AwaitingBatches.ShouldContainKey(Atlas);
        void ItShouldLeaveTheTopicAlone() => _state.Topics[Atlas].Interval.ShouldBe(TimeSpan.FromDays(4));
        void ItShouldNotReadTheRunStore() => _runStore.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task TheTopicWasPrunedWhileItsNoteWasInThePipeline()
    {
        _noteWriter.Locate(Note).Returns(NoteLocation.Processed);
        _state.Topics.Remove(Atlas);

        Result<ResolvedBatches, Error> resolved = await _handler.Handle(new ResolveAwaitingBatchesCommand(), CancellationToken.None);

        ItShouldNotRecreateTheTopic();
        ItShouldStillCompleteTheBatch();
        ItShouldNotFail();

        void ItShouldNotRecreateTheTopic() => _state.Topics.ShouldBeEmpty();
        void ItShouldStillCompleteTheBatch() => _state.AwaitingBatches.ShouldBeEmpty();
        void ItShouldNotFail() => resolved.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async Task CompletedBatchesAreOlderThanThirtyDays()
    {
        _noteWriter.Locate(Note).Returns(NoteLocation.Processing);

        await _handler.Handle(new ResolveAwaitingBatchesCommand(), CancellationToken.None);

        await ItShouldPruneThemAtTheRetentionCutOff();

        async Task ItShouldPruneThemAtTheRetentionCutOff() =>
            await _store.Received(1).PruneCompletedBatchesAsync(Now.AddDays(-30), Arg.Any<CancellationToken>());
    }

    private static AgentRun Run(string sourceNote, string tool) => new(
        "run",
        KnownAgents.ThreadWeaver,
        new AgentRunTrigger(AgentRunTriggerKind.Facts, "facts.json"),
        AgentRunStatus.Succeeded,
        Searched,
        Searched,
        null,
        null,
        [new AgentToolCall(tool, "projects/atlas/index.md", Searched, true, null)],
        FactOrigin.Research,
        Atlas,
        sourceNote);
}
