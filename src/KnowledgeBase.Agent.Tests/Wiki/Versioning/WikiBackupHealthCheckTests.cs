using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Versioning;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Wiki.Versioning;

public sealed class WikiBackupHealthCheckTests
{
    private static readonly DateTimeOffset PushedAt = new(2026, 10, 25, 9, 31, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FailedAt = new(2026, 10, 25, 11, 5, 0, TimeSpan.Zero);
    private static readonly GitCommit LastCommit = new("1a2b3c4", new DateTimeOffset(2026, 10, 25, 13, 4, 0, TimeSpan.FromHours(2)), "Editor: Tidied the garden page");

    private readonly IGitClient _git = Substitute.For<IGitClient>();
    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly WikiBackupState _state;
    private readonly HealthCheckContext _context;
    private readonly WikiBackupHealthCheck _check;
    private readonly WikiBackupHealthCheck _checkWithoutBackup;

    public WikiBackupHealthCheckTests()
    {
        IOptions<WikiConfigurationSettings> options = Substitute.For<IOptions<WikiConfigurationSettings>>();
        options.Value.Returns(new WikiConfigurationSettings { RootDirectory = Path.Combine(Path.GetTempPath(), "kb") });

        _git.HasRepository(Arg.Any<string>()).Returns(true);
        _git.GetLastCommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(LastCommit);
        _git.CountUnpushedCommitsAsync(Arg.Any<string>(), "backup", Arg.Any<CancellationToken>()).Returns(0);
        _timeProvider.GetUtcNow().Returns(PushedAt);
        _state = new WikiBackupState(_timeProvider);

        _check = new WikiBackupHealthCheck(
            _git,
            _state,
            options,
            Options.Create(new WikiBackupSettings { Remote = "git@github.com:owner/wiki-backup.git" }));
        _checkWithoutBackup = new WikiBackupHealthCheck(_git, _state, options, Options.Create(new WikiBackupSettings()));
        _context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Backup", _check, HealthStatus.Degraded, ["status"])
        };
    }

    [Test]
    public async Task TheLastPushSucceeded()
    {
        _state.RecordSuccess();

        HealthCheckResult result = await _check.CheckHealthAsync(_context, CancellationToken.None);

        ItShouldBeHealthy();
        ItShouldShowTheLastPushAndTheLastCommit();

        void ItShouldBeHealthy() => result.Status.ShouldBe(HealthStatus.Healthy);

        void ItShouldShowTheLastPushAndTheLastCommit() => result.Description.ShouldBe(
            "Last successful push: 2026-10-25 09:31 UTC. "
            + "Last commit: 1a2b3c4 \"Editor: Tidied the garden page\" at 2026-10-25 11:04 UTC. Every commit is pushed.");
    }

    [Test]
    public async Task TheLastPushFailed()
    {
        _state.RecordSuccess();
        _timeProvider.GetUtcNow().Returns(FailedAt);
        _state.RecordFailure(new GitCommandFailedError("push", "exit code 128. Host key verification failed."));
        _git.CountUnpushedCommitsAsync(Arg.Any<string>(), "backup", Arg.Any<CancellationToken>()).Returns(2);

        HealthCheckResult result = await _check.CheckHealthAsync(_context, CancellationToken.None);

        ItShouldBeDegraded();
        ItShouldShowTheErrorTheLastSuccessAndWhatIsWaiting();

        void ItShouldBeDegraded() => result.Status.ShouldBe(HealthStatus.Degraded);

        void ItShouldShowTheErrorTheLastSuccessAndWhatIsWaiting() => result.Description.ShouldBe(
            "Last push failed at 2026-10-25 11:05 UTC: 'git push' failed: exit code 128. Host key verification failed. "
            + "Last successful push: 2026-10-25 09:31 UTC. "
            + "Last commit: 1a2b3c4 \"Editor: Tidied the garden page\" at 2026-10-25 11:04 UTC. 2 commits are not pushed yet.");
    }

    [Test]
    public async Task NothingHasBeenPushedSinceTheAppStarted()
    {
        _git.CountUnpushedCommitsAsync(Arg.Any<string>(), "backup", Arg.Any<CancellationToken>()).Returns(1);

        HealthCheckResult result = await _check.CheckHealthAsync(_context, CancellationToken.None);

        ItShouldBeHealthy();
        ItShouldShowWhatIsWaiting();

        void ItShouldBeHealthy() => result.Status.ShouldBe(HealthStatus.Healthy);

        void ItShouldShowWhatIsWaiting() => result.Description.ShouldBe(
            "Last successful push: none since the app started. "
            + "Last commit: 1a2b3c4 \"Editor: Tidied the garden page\" at 2026-10-25 11:04 UTC. 1 commit is not pushed yet.");
    }

    [Test]
    public async Task NoBackupRemoteIsConfigured()
    {
        HealthCheckResult result = await _checkWithoutBackup.CheckHealthAsync(_context, CancellationToken.None);

        ItShouldBeHealthy();
        ItShouldSayBackupIsOff();
        await ItShouldNotRunGit();

        void ItShouldBeHealthy() => result.Status.ShouldBe(HealthStatus.Healthy);

        void ItShouldSayBackupIsOff() => result.Description.ShouldBe("Off; no backup remote is configured.");

        async Task ItShouldNotRunGit() => await _git.DidNotReceiveWithAnyArgs().GetLastCommitAsync(default!, default);
    }
}