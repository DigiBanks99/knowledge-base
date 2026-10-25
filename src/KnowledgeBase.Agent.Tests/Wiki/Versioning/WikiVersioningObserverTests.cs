using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Versioning;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Wiki.Versioning;

public sealed class WikiVersioningObserverTests
{
    private static readonly AgentRun Run = new(
        "20261025-093000-threadweaver-0123456789",
        KnownAgents.ThreadWeaver,
        new AgentRunTrigger(AgentRunTriggerKind.Facts, "feedback-from-susan.json"),
        AgentRunStatus.Succeeded,
        new DateTimeOffset(2026, 10, 25, 9, 30, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 25, 9, 31, 0, TimeSpan.Zero),
        "Filed Susan's feedback.",
        null,
        []);

    private readonly ILogger<WikiVersioningObserver> _logger = Substitute.For<ILogger<WikiVersioningObserver>>();
    private readonly ILogger<PushWikiCommandHandler> _pushLogger = Substitute.For<ILogger<PushWikiCommandHandler>>();
    private readonly IGitClient _git = Substitute.For<IGitClient>();
    private readonly WikiVersioningObserver _observer;

    public WikiVersioningObserverTests()
    {
        IOptions<WikiConfigurationSettings> options = Substitute.For<IOptions<WikiConfigurationSettings>>();
        options.Value.Returns(new WikiConfigurationSettings { RootDirectory = Path.Combine(Path.GetTempPath(), "kb") });
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        _pushLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        _git.HasRepository(Arg.Any<string>()).Returns(true);
        _git.StageAllAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _git.HasStagedChangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _git.CommitAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _git.SetRemoteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _git.PushAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);

        PushWikiCommandHandler pushHandler = new(
            _pushLogger,
            _git,
            new WikiBackupState(TimeProvider.System),
            options,
            Options.Create(new WikiBackupSettings { Remote = "git@github.com:owner/wiki-backup.git" }));
        _observer = new WikiVersioningObserver(_logger, new CommitWikiRunCommandHandler(_git, options), pushHandler);
    }

    [Test]
    public async Task AWikiWritingRunCommitsAChange()
    {
        await _observer.OnRunCompletedAsync(Run, CancellationToken.None);

        ItShouldPushAfterCommitting();

        void ItShouldPushAfterCommitting() => Received.InOrder(() =>
        {
            _git.CommitAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            _git.PushAsync(Arg.Any<string>(), "backup", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task AWikiWritingRunChangesNothing()
    {
        _git.HasStagedChangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        await _observer.OnRunCompletedAsync(Run, CancellationToken.None);

        await ItShouldNotPush();

        async Task ItShouldNotPush() => await _git.DidNotReceiveWithAnyArgs().PushAsync(default!, default!, default, default);
    }

    [Test]
    public async Task ThePushFails()
    {
        GitCommandFailedError failure = new("push", "exit code 128. ssh: connect to host github.com port 22: Network is unreachable");
        _git.PushAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(failure);

        Exception? exception = await Catch.ExceptionAsync(() => _observer.OnRunCompletedAsync(Run, CancellationToken.None));

        ItShouldNotThrow();
        ItShouldLogThePushFailure();

        void ItShouldNotThrow() => exception.ShouldBeNull();

        void ItShouldLogThePushFailure() => _pushLogger
            .ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call => ((EventId)call.GetArguments()[1]!).Name)
            .ShouldBe([nameof(Agent.Wiki.LoggerMessages.FailedToPushWiki)]);
    }

    [Test]
    [Arguments(KnownAgents.ThreadWeaver, AgentRunStatus.Succeeded)]
    [Arguments(KnownAgents.ThreadWeaver, AgentRunStatus.Failed)]
    [Arguments(KnownAgents.Editor, AgentRunStatus.Succeeded)]
    [Arguments(KnownAgents.Editor, AgentRunStatus.Failed)]
    public async Task AWikiWritingRunEnds(string agent, AgentRunStatus status)
    {
        await _observer.OnRunCompletedAsync(Run with { Agent = agent, Status = status }, CancellationToken.None);

        await ItShouldCommitAsTheAgent();

        async Task ItShouldCommitAsTheAgent() => await _git.Received(1).CommitAsync(
            Arg.Any<string>(),
            Arg.Is<string>(message => message.StartsWith($"{agent}: ", StringComparison.Ordinal)),
            $"{agent} <knowledge-base@localhost>",
            Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments(KnownAgents.FactFinder)]
    [Arguments(KnownAgents.Reviewer)]
    [Arguments(KnownAgents.Librarian)]
    public async Task ARunThatDoesNotWriteTheWikiEnds(string agent)
    {
        await _observer.OnRunCompletedAsync(Run with { Agent = agent }, CancellationToken.None);

        await ItShouldNotTouchTheRepository();

        async Task ItShouldNotTouchTheRepository() =>
            await _git.DidNotReceiveWithAnyArgs().StageAllAsync(default!, default);
    }

    [Test]
    public async Task GitFails()
    {
        GitCommandFailedError failure = new("commit", "git could not be started. Is it installed?");
        _git.CommitAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(failure);

        Exception? exception = await Catch.ExceptionAsync(() => _observer.OnRunCompletedAsync(Run, CancellationToken.None));

        ItShouldNotThrow();
        ItShouldLogTheFailure();

        void ItShouldNotThrow() => exception.ShouldBeNull();

        void ItShouldLogTheFailure() => _logger
            .ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call => call.GetArguments())
            .Where(arguments => (LogLevel)arguments[0]! == LogLevel.Error)
            .Select(arguments => ((EventId)arguments[1]!).Name)
            .ShouldBe([nameof(Agent.Wiki.LoggerMessages.FailedToCommitWikiRun)]);
    }

    [Test]
    public async Task TheRunsCallerHoldsTheWikiLock()
    {
        using WikiLock wikiLock = new();
        using IDisposable held = await wikiLock.AcquireAsync(CancellationToken.None);

        Task observing = _observer.OnRunCompletedAsync(Run, CancellationToken.None);
        Task finished = await Task.WhenAny(observing, Task.Delay(TimeSpan.FromSeconds(5)));

        ItShouldCommitWithoutWaitingForTheLock();

        // The lock cannot be re-entered, so an observer that tried to take it would wait here forever.
        void ItShouldCommitWithoutWaitingForTheLock() => finished.ShouldBe(observing);
    }
}
