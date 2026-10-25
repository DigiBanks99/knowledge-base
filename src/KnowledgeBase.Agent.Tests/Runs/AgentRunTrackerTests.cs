using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.Tests.Runs;

public sealed class AgentRunTrackerTests
{
    private static readonly AgentRunTrigger Trigger = new(AgentRunTriggerKind.Facts, "2026-10-25-note.json");

    private readonly ILogger<AgentRunTracker> _logger = Substitute.For<ILogger<AgentRunTracker>>();
    private readonly IAgentRunStore _store = Substitute.For<IAgentRunStore>();
    private readonly IAgentRunObserver _firstObserver = Substitute.For<IAgentRunObserver>();
    private readonly IAgentRunObserver _secondObserver = Substitute.For<IAgentRunObserver>();
    private readonly List<AgentRun> _observedRuns = [];
    private readonly AgentRunTracker _tracker;

    public AgentRunTrackerTests()
    {
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(new DateTimeOffset(2026, 10, 25, 9, 30, 0, TimeSpan.Zero));
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        _firstObserver
            .OnRunCompletedAsync(Arg.Do<AgentRun>(_observedRuns.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _tracker = new AgentRunTracker(_logger, _store, [_firstObserver, _secondObserver], timeProvider);
    }

    [Test]
    public async Task ARunSucceeds()
    {
        Result<AgentRun, Error> result = await _tracker.RunAsync(KnownAgents.ThreadWeaver, Trigger, Answer, CancellationToken.None);

        ItShouldSucceed();
        ItShouldNotifyEveryObserverOnceAfterTheRunIsSaved();
        ItShouldShowObserversTheCompletedRun();

        void ItShouldSucceed() => result.IsSuccess.ShouldBeTrue();

        void ItShouldNotifyEveryObserverOnceAfterTheRunIsSaved() => Received.InOrder(() =>
        {
            _store.SaveAsync(Arg.Is<AgentRun>(run => run.Status == AgentRunStatus.Succeeded), Arg.Any<CancellationToken>());
            _firstObserver.OnRunCompletedAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
            _secondObserver.OnRunCompletedAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        });

        void ItShouldShowObserversTheCompletedRun() => _observedRuns.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            run => run.Agent.ShouldBe(KnownAgents.ThreadWeaver),
            run => run.Trigger.ShouldBe(Trigger),
            run => run.Status.ShouldBe(AgentRunStatus.Succeeded),
            run => run.Summary.ShouldBe("Filed the facts."),
            run => run.CompletedAt.ShouldNotBeNull());
    }

    [Test]
    public async Task ARunFails()
    {
        Result<AgentRun, Error> result = await _tracker.RunAsync(
            KnownAgents.ThreadWeaver,
            Trigger,
            _ => throw new InvalidOperationException("The model server is unreachable."),
            CancellationToken.None);

        ItShouldFail();
        ItShouldShowObserversTheFailedRun();
        ItShouldNotifyTheOtherObservers();

        void ItShouldFail() => result.Error.ShouldBeOfType<AgentRunFailedError>();

        void ItShouldShowObserversTheFailedRun() => _observedRuns.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            run => run.Status.ShouldBe(AgentRunStatus.Failed),
            run => run.Error.ShouldBe("The model server is unreachable."));

        void ItShouldNotifyTheOtherObservers() =>
            _secondObserver.Received(1).OnRunCompletedAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ARunCallsTools()
    {
        await _tracker.RunAsync(
            KnownAgents.ThreadWeaver,
            Trigger,
            async _ =>
            {
                await _tracker.RecordToolCallAsync("CreateFile", "projects/foo/index.md", null);
                await _tracker.RecordToolCallAsync("PatchFile", "learning/bar.md", "The page does not exist.");
                return await Answer(CancellationToken.None);
            },
            CancellationToken.None);

        ItShouldShowObserversTheToolCallsAndTheirTargets();

        void ItShouldShowObserversTheToolCallsAndTheirTargets() => _observedRuns
            .ShouldHaveSingleItem()
            .ToolCalls.Select(call => (call.Tool, call.Target, call.Succeeded))
            .ShouldBe([("CreateFile", "projects/foo/index.md", true), ("PatchFile", "learning/bar.md", false)]);
    }

    [Test]
    public async Task ARunOverResearchStarts()
    {
        FactOrigin? originDuringTheRun = null;

        await _tracker.RunAsync(
            KnownAgents.ThreadWeaver,
            Trigger,
            FactOrigin.Research,
            cancellationToken =>
            {
                originDuringTheRun = _tracker.Current?.Origin;
                return Answer(cancellationToken);
            },
            CancellationToken.None);

        ItShouldShowToolsTheOriginDuringTheRun();
        ItShouldRecordTheOrigin();

        void ItShouldShowToolsTheOriginDuringTheRun() => originDuringTheRun.ShouldBe(FactOrigin.Research);

        void ItShouldRecordTheOrigin() => _observedRuns.ShouldHaveSingleItem().Origin.ShouldBe(FactOrigin.Research);
    }

    [Test]
    public async Task ARunStartsWithoutAnOrigin()
    {
        FactOrigin? originDuringTheRun = null;

        await _tracker.RunAsync(
            KnownAgents.ThreadWeaver,
            Trigger,
            cancellationToken =>
            {
                originDuringTheRun = _tracker.Current?.Origin;
                return Answer(cancellationToken);
            },
            CancellationToken.None);

        ItShouldTreatTheRunAsTheAuthors();

        void ItShouldTreatTheRunAsTheAuthors()
        {
            originDuringTheRun.ShouldBe(FactOrigin.Author);
            _observedRuns.ShouldHaveSingleItem().Origin.ShouldBe(FactOrigin.Author);
        }
    }

    [Test]
    public async Task AnObserverFails()
    {
        InvalidOperationException failure = new("The wiki repository is missing.");
        _firstObserver
            .OnRunCompletedAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure));

        Result<AgentRun, Error> result = await _tracker.RunAsync(KnownAgents.Editor, Trigger, Answer, CancellationToken.None);

        ItShouldStillSucceed();
        ItShouldNotifyTheOtherObservers();
        ItShouldLogTheFailure();

        void ItShouldStillSucceed() => result.IsSuccess.ShouldBeTrue();

        void ItShouldNotifyTheOtherObservers() =>
            _secondObserver.Received(1).OnRunCompletedAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());

        void ItShouldLogTheFailure() => _logger
            .ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call => call.GetArguments())
            .ShouldContain(arguments => (LogLevel)arguments[0]! == LogLevel.Error && arguments[3] == failure);
    }

    [Test]
    public async Task ARunIsCancelled()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Exception? exception = await Catch.ExceptionAsync(() => _tracker.RunAsync(
            KnownAgents.ThreadWeaver,
            Trigger,
            token => Task.FromCanceled<AgentResponse>(token),
            cancellation.Token));

        ItShouldStopTheRun();
        ItShouldNotNotifyObservers();

        void ItShouldStopTheRun() => exception.ShouldBeAssignableTo<OperationCanceledException>();

        void ItShouldNotNotifyObservers() => _observedRuns.ShouldBeEmpty();
    }

    private static Task<AgentResponse> Answer(CancellationToken cancellationToken) =>
        Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "Filed the facts.")));
}