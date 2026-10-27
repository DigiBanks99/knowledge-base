using System.Collections.Concurrent;

using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Curation;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Status;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class CurateTopicsCommandHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Atlas = "projects/atlas";

    private readonly ResearchTopics _state = new();
    private readonly IResearchStore _store;
    private readonly IModelServerHealth _modelServer = Substitute.For<IModelServerHealth>();
    private readonly IPromptTokenCounter _tokenCounter = Substitute.For<IPromptTokenCounter>();
    private readonly IWikiTopics _wikiTopics = Substitute.For<IWikiTopics>();
    private readonly ConcurrentDictionary<string, AgentRun> _runs = new();
    private readonly ScriptedAgent _pleb = new(KnownAgents.Pleb);
    private readonly ModelGate _gate;
    private readonly AgentRunTracker _runTracker;
    private readonly PlebTools _tools;
    private readonly CurateTopicsCommandHandler _handler;

    public CurateTopicsCommandHandlerTests()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Discovered);
        _state.CurationRequests[Atlas] = new CurationRequest(Atlas, ["projects/atlas/roadmap.md"], Discovered);
        _state.DenyList = new DenyList([new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied)]);
        _store = FakeResearchStore.Holding(_state);

        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _modelServer.IsAvailableToAsync(KnownAgents.Pleb, Arg.Any<CancellationToken>()).Returns(true);
        _tokenCounter.CountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success<int, Error>(500));
        _wikiTopics.ReadPage("projects/atlas/index.md").Returns(Maybe.From("# Atlas\n\nA framework I am migrating to."));
        _wikiTopics.ReadPage("projects/atlas/roadmap.md").Returns(Maybe.From("# Atlas roadmap\n\nVersion 2 next."));

        IAgentRunStore runStore = Substitute.For<IAgentRunStore>();
        runStore
            .SaveAsync(Arg.Do<AgentRun>(run => _runs[run.Id] = run), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _runTracker = new AgentRunTracker(NullLogger<AgentRunTracker>.Instance, runStore, [], timeProvider);

        IOptions<ResearchSettings> settings = Options.Create(new ResearchSettings { CurationsPerCycle = 2, CurationPromptTokenBudget = 1000 });
        _gate = new ModelGate(settings);
        _tools = new PlebTools(
            _runTracker,
            new SetTopicQueriesCommandHandler(NullLogger<SetTopicQueriesCommandHandler>.Instance, _store, timeProvider),
            new KeepTopicQueriesCommandHandler(NullLogger<KeepTopicQueriesCommandHandler>.Instance, _store, timeProvider));
        _handler = new CurateTopicsCommandHandler(
            NullLogger<CurateTopicsCommandHandler>.Instance,
            settings,
            _store,
            _wikiTopics,
            _modelServer,
            _tokenCounter,
            _gate,
            _runTracker,
            _pleb,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [$"{KnownAgents.Pleb}:Instructions"] = "Keep the topic's searches current." })
                .Build(),
            timeProvider);
    }

    public void Dispose() => _gate.Dispose();

    [Test]
    public async Task ThePlebGivesANewTopicItsQueries()
    {
        _pleb.Behaviour = async (_, token) =>
        {
            await CallToolAsync(nameof(PlebTools.SetTopicQueries), () => _tools.SetTopicQueries(Atlas, "News about the Atlas framework.", ["atlas release"], token));
            return "Set the queries.";
        };

        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldCurateTheTopic();
        ItShouldStoreTheQueries();
        ItShouldRemoveTheRequest();
        ItShouldGiveThePlebTheTopicPageAndTheChangedPages();
        ItShouldRecordACurationRunForTheTopic();

        void ItShouldCurateTheTopic() => result.Value.Curated.ShouldBe([Atlas]);

        void ItShouldStoreTheQueries() => _state.Topics[Atlas].ShouldSatisfyAllConditions(
            topic => topic.Brief.ShouldBe("News about the Atlas framework."),
            topic => topic.Queries.ShouldBe(["atlas release"]),
            topic => topic.LastCuratedAt.ShouldBe(Now));

        void ItShouldRemoveTheRequest() => _state.CurationRequests.ShouldBeEmpty();

        void ItShouldGiveThePlebTheTopicPageAndTheChangedPages() => _pleb.Prompts.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            prompt => prompt.ShouldContain("A framework I am migrating to."),
            prompt => prompt.ShouldContain("- projects/atlas/roadmap.md: Atlas roadmap"));

        void ItShouldRecordACurationRunForTheTopic() => _runs.Values.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            run => run.Agent.ShouldBe(KnownAgents.Pleb),
            run => run.Status.ShouldBe(AgentRunStatus.Succeeded),
            run => run.Trigger.ShouldBe(new AgentRunTrigger(AgentRunTriggerKind.Curation, "projects--atlas")),
            run => run.Topic.ShouldBe(Atlas));
    }

    [Test]
    public async Task ThePlebKeepsTheQueries()
    {
        _state.Topics[Atlas] = _state.Topics[Atlas] with { Brief = "News about Atlas.", Queries = ["atlas release"] };
        _pleb.Behaviour = async (_, token) =>
        {
            await CallToolAsync(nameof(PlebTools.KeepQueries), () => _tools.KeepQueries(Atlas, token));
            return "Kept the queries.";
        };

        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldCurateTheTopic();
        ItShouldKeepItsQueries();
        ItShouldRemoveTheRequest();

        void ItShouldCurateTheTopic() => result.Value.Curated.ShouldBe([Atlas]);

        void ItShouldKeepItsQueries() => _state.Topics[Atlas].ShouldSatisfyAllConditions(
            topic => topic.Queries.ShouldBe(["atlas release"]),
            topic => topic.LastCuratedAt.ShouldBe(Now));

        void ItShouldRemoveTheRequest() => _state.CurationRequests.ShouldBeEmpty();
    }

    [Test]
    public async Task ThePlebEndsWithoutAVerdict()
    {
        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldFailTheCuration();
        ItShouldMoveTheRequestToFailed();
        ItShouldRecordTheRunAsFailed();

        void ItShouldFailTheCuration() => result.Value.Failed.ShouldBe([Atlas]);

        void ItShouldMoveTheRequestToFailed()
        {
            _state.CurationRequests.ShouldBeEmpty();
            _state.FailedCurations[Atlas].Reason.ShouldContain("The Pleb ended without a verdict");
        }

        void ItShouldRecordTheRunAsFailed() => _runs.Values.ShouldHaveSingleItem().Status.ShouldBe(AgentRunStatus.Failed);
    }

    [Test]
    public async Task ThePlebCorrectsARefusedQuery()
    {
        _pleb.Behaviour = async (_, token) =>
        {
            await CallToolAsync(nameof(PlebTools.SetTopicQueries), () => _tools.SetTopicQueries(Atlas, "News about Atlas.", ["jane doe atlas"], token));
            await CallToolAsync(nameof(PlebTools.SetTopicQueries), () => _tools.SetTopicQueries(Atlas, "News about Atlas.", ["atlas release"], token));
            return "Set the queries.";
        };

        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldCurateTheTopic();
        ItShouldStoreOnlyTheCorrectedQuery();
        ItShouldRecordTheRefusalForTheAuthor();

        void ItShouldCurateTheTopic() => result.Value.Curated.ShouldBe([Atlas]);

        void ItShouldStoreOnlyTheCorrectedQuery() => _state.Topics[Atlas].Queries.ShouldBe(["atlas release"]);

        void ItShouldRecordTheRefusalForTheAuthor() => _runs.Values.ShouldHaveSingleItem().ToolCalls
            .First().Error.ShouldNotBeNull().ShouldContain("contains the name 'Jane Doe'");
    }

    [Test]
    public async Task ThePlebCuratesAnotherTopic()
    {
        _pleb.Behaviour = async (_, token) =>
        {
            await CallToolAsync(nameof(PlebTools.SetTopicQueries), () => _tools.SetTopicQueries("projects/other", "News.", ["other release"], token));
            return "Set the queries.";
        };

        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldFailTheCuration();
        ItShouldTellThePlebWhichTopicItIsCurating();

        void ItShouldFailTheCuration() => result.Value.Failed.ShouldBe([Atlas]);

        void ItShouldTellThePlebWhichTopicItIsCurating() => _runs.Values.ShouldHaveSingleItem().ToolCalls.Single()
            .Error.ShouldBe("This curation is for topic `projects/atlas`, not `projects/other`. Call the tool again with `topic` set to `projects/atlas`.");
    }

    [Test]
    public async Task ThePromptIsOverBudget()
    {
        _tokenCounter.CountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success<int, Error>(1001));

        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldFailTheCuration();
        ItShouldNotRunThePleb();
        ItShouldSayHowFarOverItIs();

        void ItShouldFailTheCuration() => result.Value.Failed.ShouldBe([Atlas]);

        void ItShouldNotRunThePleb() => _pleb.Prompts.ShouldBeEmpty();

        void ItShouldSayHowFarOverItIs() =>
            _state.FailedCurations[Atlas].Reason.ShouldStartWith("The curation prompt is 1001 tokens, over the budget of 1000.");
    }

    [Test]
    public async Task ThePromptIsCounted()
    {
        _pleb.Behaviour = async (_, token) =>
        {
            await CallToolAsync(nameof(PlebTools.SetTopicQueries), () => _tools.SetTopicQueries(Atlas, "News about Atlas.", ["atlas release"], token));
            return "Set the queries.";
        };

        await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldCountTheInstructionsWithThePrompt();

        void ItShouldCountTheInstructionsWithThePrompt() => _tokenCounter.Received(1).CountAsync(
            Arg.Is<string>(text => text.StartsWith("# Your Role: Pleb") && text.Contains("Keep the topic's searches current.") && text.Contains("Curate the research topic")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TheRunFailsAfterThePlebGaveItsVerdict()
    {
        _pleb.Behaviour = async (_, token) =>
        {
            await CallToolAsync(nameof(PlebTools.SetTopicQueries), () => _tools.SetTopicQueries(Atlas, "News about Atlas.", ["atlas release"], token));
            throw new InvalidOperationException("The server answered 500.");
        };

        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldCountAsCurated();
        ItShouldNotRecordAFailedCuration();

        void ItShouldCountAsCurated()
        {
            result.Value.Curated.ShouldBe([Atlas]);
            _state.CurationRequests.ShouldBeEmpty();
        }

        void ItShouldNotRecordAFailedCuration() => _state.FailedCurations.ShouldBeEmpty();
    }

    [Test]
    public async Task ThePromptCannotBeCounted()
    {
        _tokenCounter.CountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<int, Error>(new TokenCountUnavailableError("connection refused")));

        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldLeaveTheRequestWaiting();
        ItShouldNotRunThePleb();

        void ItShouldLeaveTheRequestWaiting()
        {
            result.Value.LeftWaiting.ShouldBe([Atlas]);
            _state.CurationRequests.Keys.ShouldBe([Atlas]);
            _state.FailedCurations.ShouldBeEmpty();
        }

        void ItShouldNotRunThePleb() => _pleb.Prompts.ShouldBeEmpty();
    }

    [Test]
    public async Task TheModelTimesOut()
    {
        _pleb.Behaviour = (_, _) => throw new TaskCanceledException("The operation was cancelled because it exceeded the timeout.");

        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldLeaveTheRequestWaiting();

        void ItShouldLeaveTheRequestWaiting()
        {
            result.Value.LeftWaiting.ShouldBe([Atlas]);
            _state.CurationRequests.Keys.ShouldBe([Atlas]);
            _state.FailedCurations.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task TheModelServerIsUnhealthy()
    {
        _modelServer.IsAvailableToAsync(KnownAgents.Pleb, Arg.Any<CancellationToken>()).Returns(false);

        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldSkipTheCycle();
        ItShouldNotRunThePleb();
        ItShouldLeaveTheRequestWaiting();

        void ItShouldSkipTheCycle() => result.Value.Skipped.ShouldBeTrue();

        void ItShouldNotRunThePleb() => _pleb.Prompts.ShouldBeEmpty();

        void ItShouldLeaveTheRequestWaiting() => _state.CurationRequests.Keys.ShouldBe([Atlas]);
    }

    [Test]
    public async Task AMutedTopicIsWaiting()
    {
        _state.Topics[Atlas] = _state.Topics[Atlas] with { Muted = true };

        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldNotCurateIt();
        ItShouldLeaveTheRequestWaiting();

        void ItShouldNotCurateIt()
        {
            result.Value.Curated.ShouldBeEmpty();
            _pleb.Prompts.ShouldBeEmpty();
        }

        void ItShouldLeaveTheRequestWaiting() => _state.CurationRequests.Keys.ShouldBe([Atlas]);
    }

    [Test]
    public async Task MoreTopicsAreWaitingThanACycleCurates()
    {
        foreach ((string topic, int hours) in new[] { ("projects/newest", 3), ("learning/oldest.md", -3), ("plans/middle.md", 1) })
        {
            _state.Topics[topic] = ResearchTopic.New(topic, Discovered);
            _state.CurationRequests[topic] = new CurationRequest(topic, [], Discovered.AddHours(hours));
        }

        List<string> curated = [];
        _pleb.Behaviour = async (_, token) =>
        {
            string topic = _runTracker.Current!.Topic!;
            curated.Add(topic);
            await CallToolAsync(nameof(PlebTools.SetTopicQueries), () => _tools.SetTopicQueries(topic, "News.", ["release notes"], token));
            return "Set the queries.";
        };

        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldCurateTheOldestRequestsUpToTheCap();
        ItShouldLeaveTheRestWaiting();

        void ItShouldCurateTheOldestRequestsUpToTheCap()
        {
            curated.ShouldBe(["learning/oldest.md", Atlas]);
            result.Value.Curated.ShouldBe(["learning/oldest.md", Atlas]);
        }

        void ItShouldLeaveTheRestWaiting() =>
            _state.CurationRequests.Keys.Order(StringComparer.Ordinal).ShouldBe(["plans/middle.md", "projects/newest"]);
    }

    [Test]
    public async Task TheTopicChangesWhileItIsCurated()
    {
        _pleb.Behaviour = async (_, token) =>
        {
            await _store.RequestCurationAsync(new CurationRequest(Atlas, ["projects/atlas/v2.md"], Now), token);
            await CallToolAsync(nameof(PlebTools.SetTopicQueries), () => _tools.SetTopicQueries(Atlas, "News about Atlas.", ["atlas release"], token));
            return "Set the queries.";
        };

        Result<CurationCycle, Error> result = await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldCurateTheTopic();
        ItShouldKeepTheNewerChangesWaiting();

        void ItShouldCurateTheTopic() => result.Value.Curated.ShouldBe([Atlas]);

        void ItShouldKeepTheNewerChangesWaiting() =>
            _state.CurationRequests[Atlas].ChangedPaths.ShouldBe(["projects/atlas/roadmap.md", "projects/atlas/v2.md"]);
    }

    [Test]
    public async Task ACurationSucceedsAfterAnEarlierOneFailed()
    {
        _state.FailedCurations[Atlas] = new FailedCuration(new CurationRequest(Atlas, [], Discovered), "No verdict.", Discovered);
        _pleb.Behaviour = async (_, token) =>
        {
            await CallToolAsync(nameof(PlebTools.SetTopicQueries), () => _tools.SetTopicQueries(Atlas, "News about Atlas.", ["atlas release"], token));
            return "Set the queries.";
        };

        await _handler.Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldClearTheFailure();

        void ItShouldClearTheFailure() => _state.FailedCurations.ShouldBeEmpty();
    }

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
