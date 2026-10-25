using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Versioning;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Wiki.Versioning;

public sealed class InitializeWikiRepositoryCommandHandlerTests
{
    private readonly IGitClient _git = Substitute.For<IGitClient>();
    private readonly string _wikiDirectory;
    private readonly InitializeWikiRepositoryCommandHandler _handler;

    public InitializeWikiRepositoryCommandHandlerTests()
    {
        IOptions<WikiConfigurationSettings> options = Substitute.For<IOptions<WikiConfigurationSettings>>();
        options.Value.Returns(new WikiConfigurationSettings { RootDirectory = Path.Combine(Path.GetTempPath(), "kb") });
        _wikiDirectory = options.Value.WikiDirectory;

        _git.HasRepository(_wikiDirectory).Returns(false);
        _git.InitAsync(_wikiDirectory, Arg.Any<CancellationToken>()).Returns(true);
        _git.SetConfigAsync(_wikiDirectory, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _git.StageAllAsync(_wikiDirectory, Arg.Any<CancellationToken>()).Returns(true);
        _git.HasStagedChangesAsync(_wikiDirectory, Arg.Any<CancellationToken>()).Returns(true);
        _git.CommitAsync(_wikiDirectory, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        _handler = new InitializeWikiRepositoryCommandHandler(
            NullLogger<InitializeWikiRepositoryCommandHandler>.Instance,
            _git,
            options);
    }

    [Test]
    public async Task AnExistingWikiHasNoRepository()
    {
        Result<bool, Error> result = await _handler.Handle(new InitializeWikiRepositoryCommand(), CancellationToken.None);

        ItShouldReportTheRepositoryAsCreated();
        ItShouldCreateAndConfigureTheRepositoryBeforeCommittingThePages();

        void ItShouldReportTheRepositoryAsCreated() => result.Value.ShouldBeTrue();

        void ItShouldCreateAndConfigureTheRepositoryBeforeCommittingThePages() => Received.InOrder(() =>
        {
            _git.InitAsync(_wikiDirectory, Arg.Any<CancellationToken>());
            _git.SetConfigAsync(_wikiDirectory, "core.autocrlf", "false", Arg.Any<CancellationToken>());
            _git.SetConfigAsync(_wikiDirectory, "user.name", "Knowledge Base", Arg.Any<CancellationToken>());
            _git.SetConfigAsync(_wikiDirectory, "user.email", "knowledge-base@localhost", Arg.Any<CancellationToken>());
            _git.StageAllAsync(_wikiDirectory, Arg.Any<CancellationToken>());
            _git.HasStagedChangesAsync(_wikiDirectory, Arg.Any<CancellationToken>());
            _git.CommitAsync(_wikiDirectory, "Initial wiki", "Knowledge Base <knowledge-base@localhost>", Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task AnEmptyWikiHasNoRepository()
    {
        _git.HasStagedChangesAsync(_wikiDirectory, Arg.Any<CancellationToken>()).Returns(false);

        Result<bool, Error> result = await _handler.Handle(new InitializeWikiRepositoryCommand(), CancellationToken.None);

        ItShouldReportTheRepositoryAsCreated();
        await ItShouldNotMakeAnEmptyCommit();

        void ItShouldReportTheRepositoryAsCreated() => result.Value.ShouldBeTrue();

        async Task ItShouldNotMakeAnEmptyCommit() =>
            await _git.DidNotReceiveWithAnyArgs().CommitAsync(default!, default!, default!, default);
    }

    [Test]
    public async Task TheWikiIsAlreadyARepository()
    {
        _git.HasRepository(_wikiDirectory).Returns(true);

        Result<bool, Error> result = await _handler.Handle(new InitializeWikiRepositoryCommand(), CancellationToken.None);

        ItShouldReportNothingCreated();
        await ItShouldKeepTheHistoryAsItIs();
        await ItShouldStillApplyTheRepositorySettings();

        void ItShouldReportNothingCreated() => result.Value.ShouldBeFalse();

        async Task ItShouldKeepTheHistoryAsItIs()
        {
            await _git.DidNotReceiveWithAnyArgs().InitAsync(default!, default);
            await _git.DidNotReceiveWithAnyArgs().CommitAsync(default!, default!, default!, default);
        }

        // A repository restored from elsewhere, or one an earlier start left half set up, still needs an identity.
        async Task ItShouldStillApplyTheRepositorySettings()
        {
            await _git.Received(1).SetConfigAsync(_wikiDirectory, "core.autocrlf", "false", Arg.Any<CancellationToken>());
            await _git.Received(1).SetConfigAsync(_wikiDirectory, "user.name", "Knowledge Base", Arg.Any<CancellationToken>());
            await _git.Received(1).SetConfigAsync(_wikiDirectory, "user.email", "knowledge-base@localhost", Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task GitIsNotInstalled()
    {
        GitCommandFailedError failure = new("init", "git could not be started. Is it installed?");
        _git.InitAsync(_wikiDirectory, Arg.Any<CancellationToken>()).Returns(failure);

        Result<bool, Error> result = await _handler.Handle(new InitializeWikiRepositoryCommand(), CancellationToken.None);

        ItShouldReturnTheFailure();
        await ItShouldStopThere();

        void ItShouldReturnTheFailure() => result.Error.ShouldBe(failure);

        async Task ItShouldStopThere() =>
            await _git.DidNotReceiveWithAnyArgs().SetConfigAsync(default!, default!, default!, default);
    }
}