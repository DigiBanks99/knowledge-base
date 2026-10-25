using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Versioning;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Wiki.Versioning;

public sealed class PushWikiCommandHandlerTests
{
    private const string BackupRemote = "git@github.com:owner/wiki-backup.git";
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 31, 0, TimeSpan.Zero);

    private readonly IGitClient _git = Substitute.For<IGitClient>();
    private readonly WikiBackupState _state;
    private readonly string _wikiDirectory;
    private readonly PushWikiCommandHandler _handler;
    private readonly PushWikiCommandHandler _handlerWithoutBackup;

    public PushWikiCommandHandlerTests()
    {
        IOptions<WikiConfigurationSettings> options = Substitute.For<IOptions<WikiConfigurationSettings>>();
        options.Value.Returns(new WikiConfigurationSettings { RootDirectory = Path.Combine(Path.GetTempPath(), "kb") });
        _wikiDirectory = options.Value.WikiDirectory;

        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _state = new WikiBackupState(timeProvider);

        _git.SetRemoteAsync(_wikiDirectory, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _git.CountUnpushedCommitsAsync(_wikiDirectory, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(0);
        _git.PushAsync(_wikiDirectory, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);

        _handler = new PushWikiCommandHandler(
            NullLogger<PushWikiCommandHandler>.Instance,
            _git,
            _state,
            options,
            Options.Create(new WikiBackupSettings
            {
                Remote = BackupRemote,
                KeyDirectory = "/run/wiki-backup"
            }));
        _handlerWithoutBackup = new PushWikiCommandHandler(
            NullLogger<PushWikiCommandHandler>.Instance,
            _git,
            _state,
            options,
            Options.Create(new WikiBackupSettings()));
    }

    [Test]
    public async Task ThePushSucceeds()
    {
        Result<bool, Error> result = await _handler.Handle(new PushWikiCommand(), CancellationToken.None);

        ItShouldReportAPush();
        ItShouldPointTheRemoteAtTheConfiguredUrlFirst();
        await ItShouldPushToTheBackupRemoteWithTheDeployKey();
        ItShouldRecordTheSuccess();

        void ItShouldReportAPush() => result.Value.ShouldBeTrue();

        void ItShouldPointTheRemoteAtTheConfiguredUrlFirst() => Received.InOrder(() =>
        {
            _git.SetRemoteAsync(_wikiDirectory, "backup", BackupRemote, Arg.Any<CancellationToken>());
            _git.PushAsync(_wikiDirectory, "backup", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        });

        async Task ItShouldPushToTheBackupRemoteWithTheDeployKey() => await _git.Received(1).PushAsync(
            _wikiDirectory,
            "backup",
            Arg.Is<string?>(command => command != null && command.Contains("'/run/wiki-backup/id_ed25519'", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());

        void ItShouldRecordTheSuccess() => _state.Outcome.ShouldBe(new WikiBackupOutcome(Now, Now, null));
    }

    [Test]
    public async Task ThePushFails()
    {
        GitCommandFailedError failure = new("push", "exit code 128. Host key verification failed.");
        _git.PushAsync(_wikiDirectory, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(failure);

        Result<bool, Error> result = await _handler.Handle(new PushWikiCommand(), CancellationToken.None);

        ItShouldReturnTheFailure();
        ItShouldRecordTheErrorWithoutASuccess();

        void ItShouldReturnTheFailure() => result.Error.ShouldBe(failure);

        void ItShouldRecordTheErrorWithoutASuccess() =>
            _state.Outcome.ShouldBe(new WikiBackupOutcome(Now, null, failure.Message));
    }

    [Test]
    public async Task APushSucceedsAfterAFailedOne()
    {
        _git.PushAsync(_wikiDirectory, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandFailedError("push", "exit code 128."), Result.Success<bool, Error>(true));

        await _handler.Handle(new PushWikiCommand(), CancellationToken.None);
        Result<bool, Error> result = await _handler.Handle(new PushWikiCommand(), CancellationToken.None);

        ItShouldReportAPush();
        ItShouldClearTheError();

        void ItShouldReportAPush() => result.Value.ShouldBeTrue();

        void ItShouldClearTheError() => _state.Outcome.LastPushFailed.ShouldBeFalse();
    }

    [Test]
    public async Task TheRemoteUrlIsRejected()
    {
        GitCommandFailedError failure = new("remote", "exit code 128. fatal: could not lock config file");
        _git.SetRemoteAsync(_wikiDirectory, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(failure);

        Result<bool, Error> result = await _handler.Handle(new PushWikiCommand(), CancellationToken.None);

        ItShouldReturnTheFailure();
        ItShouldRecordItForTheStatusPage();
        await ItShouldNotPush();

        void ItShouldReturnTheFailure() => result.Error.ShouldBe(failure);

        void ItShouldRecordItForTheStatusPage() => _state.Outcome.LastError.ShouldBe(failure.Message);

        async Task ItShouldNotPush() => await _git.DidNotReceiveWithAnyArgs().PushAsync(default!, default!, default, default);
    }

    [Test]
    public async Task TheAppStartsWithEverythingPushed()
    {
        Result<bool, Error> result = await _handler.Handle(new PushWikiCommand(OnlyWhenBehind: true), CancellationToken.None);

        ItShouldReportNoPush();
        await ItShouldNotContactTheRemote();

        void ItShouldReportNoPush() => result.Value.ShouldBeFalse();

        async Task ItShouldNotContactTheRemote() =>
            await _git.DidNotReceiveWithAnyArgs().PushAsync(default!, default!, default, default);
    }

    [Test]
    public async Task TheAppStartsWithCommitsLeftUnpushed()
    {
        _git.CountUnpushedCommitsAsync(_wikiDirectory, "backup", Arg.Any<CancellationToken>()).Returns(2);

        Result<bool, Error> result = await _handler.Handle(new PushWikiCommand(OnlyWhenBehind: true), CancellationToken.None);

        ItShouldReportAPush();

        void ItShouldReportAPush() => result.Value.ShouldBeTrue();
    }

    [Test]
    public async Task NoBackupRemoteIsConfigured()
    {
        Result<bool, Error> result = await _handlerWithoutBackup.Handle(new PushWikiCommand(), CancellationToken.None);

        ItShouldReportNoPush();
        await ItShouldNotRunGit();
        ItShouldRecordNothing();

        void ItShouldReportNoPush() => result.Value.ShouldBeFalse();

        async Task ItShouldNotRunGit()
        {
            await _git.DidNotReceiveWithAnyArgs().SetRemoteAsync(default!, default!, default!, default);
            await _git.DidNotReceiveWithAnyArgs().PushAsync(default!, default!, default, default);
        }

        void ItShouldRecordNothing() => _state.Outcome.ShouldBe(WikiBackupOutcome.None);
    }
}