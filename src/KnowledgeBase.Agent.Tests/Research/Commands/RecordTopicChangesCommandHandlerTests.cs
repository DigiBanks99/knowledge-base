using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class RecordTopicChangesCommandHandlerTests
{
    private const string KnownTopic = "projects/known";

    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Discovered = Now.AddDays(-7);

    private readonly ResearchTopics _state = new();
    private readonly IResearchStore _store;
    private readonly IWikiTopics _wikiTopics = Substitute.For<IWikiTopics>();
    private readonly WikiPaths _wikiPaths;
    private readonly RecordTopicChangesCommandHandler _handler;

    public RecordTopicChangesCommandHandlerTests()
    {
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _wikiTopics.Exists(Arg.Any<string>()).Returns(true);
        _state.Topics[KnownTopic] = ResearchTopic.New(KnownTopic, Discovered) with { Brief = "A known project.", Muted = true };
        _store = FakeResearchStore.Holding(_state);
        _wikiPaths = new WikiPaths(Options.Create(new WikiConfigurationSettings { RootDirectory = "/data/kb" }));
        _handler = new RecordTopicChangesCommandHandler(
            NullLogger<RecordTopicChangesCommandHandler>.Instance,
            _store,
            _wikiTopics,
            _wikiPaths,
            timeProvider);
    }

    [Test]
    public async Task TheThreadWeaverCreatesATopic()
    {
        Result<TopicChanges, Error> result = await _handler.Handle(
            Run(KnownAgents.ThreadWeaver, Call("CreateDirectory", "projects/new/"), Call("CreateFile", "projects/new/index.md")),
            CancellationToken.None);

        ItShouldCreateItsState();
        ItShouldRequestCurationWithTheChangedPaths();
        ItShouldReportIt();

        void ItShouldCreateItsState() => _state.Topics["projects/new"].ShouldSatisfyAllConditions(
            topic => topic.Path.ShouldBe("projects/new"),
            topic => topic.Brief.ShouldBeNull(),
            topic => topic.Queries.ShouldBeEmpty(),
            topic => topic.Muted.ShouldBeFalse(),
            topic => topic.CreatedAt.ShouldBe(Now));

        void ItShouldRequestCurationWithTheChangedPaths() => _state.CurationRequests["projects/new"].ShouldSatisfyAllConditions(
            request => request.ChangedPaths.ShouldBe(["projects/new/", "projects/new/index.md"]),
            request => request.RequestedAt.ShouldBe(Now));

        void ItShouldReportIt() => result.Value.ShouldSatisfyAllConditions(
            changes => changes.Created.ShouldBe(["projects/new"]),
            changes => changes.CurationRequested.ShouldBe(["projects/new"]),
            changes => changes.Pruned.ShouldBeEmpty());
    }

    [Test]
    public async Task TheThreadWeaverChangesAKnownTopic()
    {
        await _handler.Handle(Run(KnownAgents.ThreadWeaver, Call("PatchFile", "projects/known/index.md")), CancellationToken.None);

        ItShouldRequestCuration();
        ItShouldKeepTheTopicsState();

        void ItShouldRequestCuration() =>
            _state.CurationRequests[KnownTopic].ChangedPaths.ShouldBe(["projects/known/index.md"]);

        void ItShouldKeepTheTopicsState() => _state.Topics[KnownTopic].ShouldSatisfyAllConditions(
            topic => topic.Brief.ShouldBe("A known project."),
            topic => topic.Muted.ShouldBeTrue(),
            topic => topic.CreatedAt.ShouldBe(Discovered));
    }

    [Test]
    public async Task TheThreadWeaverChangesATopicTheAuthorEdited()
    {
        _state.Topics[KnownTopic] = _state.Topics[KnownTopic] with { Pinned = true };

        TopicChanges changes = (await _handler.Handle(Run(KnownAgents.ThreadWeaver, Call("PatchFile", "projects/known/index.md")), CancellationToken.None)).Value;

        ItShouldNotRequestCuration();
        ItShouldKeepTheTopic();

        void ItShouldNotRequestCuration()
        {
            changes.CurationRequested.ShouldBeEmpty();
            _state.CurationRequests.ShouldBeEmpty();
        }

        void ItShouldKeepTheTopic() => _state.Topics[KnownTopic].Pinned.ShouldBeTrue();
    }

    [Test]
    public async Task TheThreadWeaverChangesAKnownTopicTwice()
    {
        await _handler.Handle(Run(KnownAgents.ThreadWeaver, Call("PatchFile", "projects/known/a.md")), CancellationToken.None);
        await _handler.Handle(Run(KnownAgents.ThreadWeaver, Call("CreateFile", "projects/known/b.md")), CancellationToken.None);

        ItShouldKeepOneRequestWithBothChanges();

        void ItShouldKeepOneRequestWithBothChanges() => _state.CurationRequests.Values.ShouldHaveSingleItem()
            .ChangedPaths.ShouldBe(["projects/known/a.md", "projects/known/b.md"]);
    }

    [Test]
    public async Task TheEditorRestructuresAKnownTopic()
    {
        await _handler.Handle(
            Run(KnownAgents.Editor, Call("MoveFile", "projects/known/a.md → projects/known/archive/a.md")),
            CancellationToken.None);

        ItShouldNotRequestCuration();
        ItShouldKeepTheTopic();

        void ItShouldNotRequestCuration() => _state.CurationRequests.ShouldBeEmpty();

        void ItShouldKeepTheTopic() => _state.Topics.Keys.ShouldBe([KnownTopic]);
    }

    [Test]
    public async Task TheEditorMovesATopicToAnotherSection()
    {
        _wikiTopics.Exists(KnownTopic).Returns(false);

        Result<TopicChanges, Error> result = await _handler.Handle(
            Run(KnownAgents.Editor, Call("MoveFile", "projects/known/index.md → learning/known.md")),
            CancellationToken.None);

        ItShouldPruneTheOldTopic();
        ItShouldStartTheNewOneFresh();
        ItShouldRequestCurationForTheNewOne();

        void ItShouldPruneTheOldTopic() => result.Value.Pruned.ShouldBe([KnownTopic]);

        void ItShouldStartTheNewOneFresh() => _state.Topics.Values.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            topic => topic.Path.ShouldBe("learning/known.md"),
            topic => topic.Muted.ShouldBeFalse());

        void ItShouldRequestCurationForTheNewOne() => _state.CurationRequests.Keys.ShouldBe(["learning/known.md"]);
    }

    [Test]
    public async Task ATopicsPageIsDeleted()
    {
        _wikiTopics.Exists(KnownTopic).Returns(false);
        _state.CurationRequests[KnownTopic] = new CurationRequest(KnownTopic, [], Discovered);

        await _handler.Handle(Run(KnownAgents.ThreadWeaver, Call("DeleteFile", "projects/known/index.md")), CancellationToken.None);

        ItShouldDropTheTopicsState();
        ItShouldDropItsWaitingRequest();

        void ItShouldDropTheTopicsState() => _state.Topics.ShouldBeEmpty();

        void ItShouldDropItsWaitingRequest() => _state.CurationRequests.ShouldBeEmpty();
    }

    [Test]
    public async Task OnePageOfAFolderTopicIsDeleted()
    {
        await _handler.Handle(Run(KnownAgents.Editor, Call("DeleteFile", "projects/known/old.md")), CancellationToken.None);

        ItShouldKeepTheTopic();

        void ItShouldKeepTheTopic() => _state.Topics.Keys.ShouldBe([KnownTopic]);
    }

    [Test]
    public async Task APageIsCreatedAndDeletedInOneRun()
    {
        _wikiTopics.Exists("learning/scratch.md").Returns(false);

        Result<TopicChanges, Error> result = await _handler.Handle(
            Run(KnownAgents.ThreadWeaver, Call("CreateFile", "learning/scratch.md"), Call("DeleteFile", "learning/scratch.md")),
            CancellationToken.None);

        ItShouldLeaveNoTrace();

        void ItShouldLeaveNoTrace() => result.Value.ShouldSatisfyAllConditions(
            changes => changes.Created.ShouldBeEmpty(),
            changes => changes.Pruned.ShouldBeEmpty(),
            changes => changes.CurationRequested.ShouldBeEmpty());
    }

    [Test]
    [Arguments("projects/index.md")]
    [Arguments("people/jane-doe/index.md")]
    [Arguments("decisions/use-git.md")]
    public async Task ARunChangesAPageThatIsNotATopic(string wikiPath)
    {
        await _handler.Handle(Run(KnownAgents.ThreadWeaver, Call("PatchFile", wikiPath)), CancellationToken.None);

        ItShouldIgnoreIt();

        void ItShouldIgnoreIt() => _state.ShouldSatisfyAllConditions(
            state => state.Topics.Keys.ShouldBe([KnownTopic]),
            state => state.CurationRequests.ShouldBeEmpty());
    }

    [Test]
    public async Task AToolCallFailsOrOnlyReads()
    {
        await _handler.Handle(
            Run(
                KnownAgents.ThreadWeaver,
                Call("CreateFile", "projects/failed/index.md") with { Succeeded = false, Error = "The file already exists." },
                Call("ReadFile", "projects/read/index.md")),
            CancellationToken.None);

        ItShouldIgnoreThem();

        void ItShouldIgnoreThem() => _state.Topics.Keys.ShouldBe([KnownTopic]);
    }

    [Test]
    public async Task TheAgentNamesAPageInAnotherWay()
    {
        string absolutePath = Path.Combine(_wikiPaths.Root, "projects", "absolute", "index.md");

        await _handler.Handle(
            Run(KnownAgents.ThreadWeaver, Call("CreateFile", absolutePath), Call("CreateFile", "projects/x/../dotted/index.md")),
            CancellationToken.None);

        ItShouldFindTheirTopics();

        void ItShouldFindTheirTopics() =>
            _state.Topics.Keys.ShouldBe([KnownTopic, "projects/absolute", "projects/dotted"], ignoreOrder: true);
    }

    [Test]
    public async Task OneTopicsStateCannotBeUpdated()
    {
        ResearchStoreError failure = new("research/topics/projects--broken.json", "The file is in use.");
        _store.UpdateTopicAsync("projects/broken", Arg.Any<Func<Maybe<ResearchTopic>, Result<Maybe<ResearchTopic>, Error>>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Maybe<ResearchTopic>, Error>(failure));

        Result<TopicChanges, Error> result = await _handler.Handle(
            Run(KnownAgents.ThreadWeaver, Call("CreateFile", "projects/broken/index.md"), Call("CreateFile", "projects/fine/index.md")),
            CancellationToken.None);

        ItShouldStillRecordTheOtherTopics();
        ItShouldReturnTheFailure();

        void ItShouldStillRecordTheOtherTopics() => _state.Topics.Keys.ShouldContain("projects/fine");

        void ItShouldReturnTheFailure() => result.Error.ShouldBe(failure);
    }

    [Test]
    public async Task AnotherAgentsRunEnds()
    {
        await _handler.Handle(Run(KnownAgents.FactFinder, Call("CreateFile", "projects/new/index.md")), CancellationToken.None);

        ItShouldIgnoreIt();

        void ItShouldIgnoreIt() => _state.Topics.Keys.ShouldBe([KnownTopic]);
    }

    [Test]
    public async Task AFailedRunChangedATopic()
    {
        RecordTopicChangesCommand command = Run(KnownAgents.ThreadWeaver, Call("CreateFile", "plans/q4.md"));

        await _handler.Handle(command with { Run = command.Run with { Status = AgentRunStatus.Failed } }, CancellationToken.None);

        ItShouldStillDiscoverIt();

        void ItShouldStillDiscoverIt() => _state.Topics.Keys.ShouldContain("plans/q4.md");
    }

    private static AgentToolCall Call(string tool, string target) => new(tool, target, Now, true, null);

    private static RecordTopicChangesCommand Run(string agent, params AgentToolCall[] toolCalls) => new(new AgentRun(
        $"20261025-093000-{agent.ToLowerInvariant()}",
        agent,
        new AgentRunTrigger(AgentRunTriggerKind.Facts, "note.json"),
        AgentRunStatus.Succeeded,
        Now,
        Now,
        null,
        null,
        toolCalls));
}
