using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Versioning;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Wiki.Versioning;

public sealed class CommitWikiRunCommandHandlerTests
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

    private readonly IGitClient _git = Substitute.For<IGitClient>();
    private readonly string _wikiDirectory;
    private readonly CommitWikiRunCommandHandler _handler;

    public CommitWikiRunCommandHandlerTests()
    {
        IOptions<WikiConfigurationSettings> options = Substitute.For<IOptions<WikiConfigurationSettings>>();
        options.Value.Returns(new WikiConfigurationSettings { RootDirectory = Path.Combine(Path.GetTempPath(), "kb") });
        _wikiDirectory = options.Value.WikiDirectory;

        _git.HasRepository(_wikiDirectory).Returns(true);
        _git.StageAllAsync(_wikiDirectory, Arg.Any<CancellationToken>()).Returns(true);
        _git.HasStagedChangesAsync(_wikiDirectory, Arg.Any<CancellationToken>()).Returns(true);
        _git.CommitAsync(_wikiDirectory, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        _handler = new CommitWikiRunCommandHandler(_git, options);
    }

    [Test]
    public async Task ARunChangedTheWiki()
    {
        Result<bool, Error> result = await _handler.Handle(new CommitWikiRunCommand(Run), CancellationToken.None);

        ItShouldReportACommit();
        ItShouldStageEverythingBeforeCommitting();
        await ItShouldCommitAsTheAgentWithTheRunsMessage();

        void ItShouldReportACommit() => result.Value.ShouldBeTrue();

        void ItShouldStageEverythingBeforeCommitting() => Received.InOrder(() =>
        {
            _git.StageAllAsync(_wikiDirectory, Arg.Any<CancellationToken>());
            _git.HasStagedChangesAsync(_wikiDirectory, Arg.Any<CancellationToken>());
            _git.CommitAsync(_wikiDirectory, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        });

        async Task ItShouldCommitAsTheAgentWithTheRunsMessage() => await _git.Received(1).CommitAsync(
            _wikiDirectory,
            WikiCommitMessage.For(Run),
            "ThreadWeaver <knowledge-base@localhost>",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AFailedRunLeftPartialEdits()
    {
        AgentRun failed = Run with
        {
            Agent = KnownAgents.Editor,
            Status = AgentRunStatus.Failed,
            Summary = null,
            Error = "The model server is unreachable."
        };

        Result<bool, Error> result = await _handler.Handle(new CommitWikiRunCommand(failed), CancellationToken.None);

        ItShouldReportACommit();
        await ItShouldCommitTheFailedRunAsTheEditor();

        void ItShouldReportACommit() => result.Value.ShouldBeTrue();

        async Task ItShouldCommitTheFailedRunAsTheEditor() => await _git.Received(1).CommitAsync(
            _wikiDirectory,
            Arg.Is<string>(message => message.StartsWith("Editor: The model server is unreachable.\n", StringComparison.Ordinal)
                && message.Contains("\nStatus: Failed\n", StringComparison.Ordinal)),
            "Editor <knowledge-base@localhost>",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ARunChangedNothing()
    {
        _git.HasStagedChangesAsync(_wikiDirectory, Arg.Any<CancellationToken>()).Returns(false);

        Result<bool, Error> result = await _handler.Handle(new CommitWikiRunCommand(Run), CancellationToken.None);

        ItShouldReportNoCommit();
        await ItShouldNotCommit();

        void ItShouldReportNoCommit() => result.Value.ShouldBeFalse();

        async Task ItShouldNotCommit() => await _git.DidNotReceiveWithAnyArgs().CommitAsync(default!, default!, default!, default);
    }

    [Test]
    public async Task TheWikiIsNotARepository()
    {
        _git.HasRepository(_wikiDirectory).Returns(false);

        Result<bool, Error> result = await _handler.Handle(new CommitWikiRunCommand(Run), CancellationToken.None);

        ItShouldFail();
        await ItShouldNotRunGit();

        void ItShouldFail() => result.Error.ShouldBeOfType<WikiRepositoryMissingError>();

        async Task ItShouldNotRunGit() => await _git.DidNotReceiveWithAnyArgs().StageAllAsync(default!, default);
    }

    [Test]
    public async Task GitFails()
    {
        GitCommandFailedError failure = new("add", "exit code 128. fatal: index.lock exists");
        _git.StageAllAsync(_wikiDirectory, Arg.Any<CancellationToken>()).Returns(failure);

        Result<bool, Error> result = await _handler.Handle(new CommitWikiRunCommand(Run), CancellationToken.None);

        ItShouldReturnTheFailure();
        await ItShouldNotCommit();

        void ItShouldReturnTheFailure() => result.Error.ShouldBe(failure);

        async Task ItShouldNotCommit() => await _git.DidNotReceiveWithAnyArgs().CommitAsync(default!, default!, default!, default);
    }
}
