using System.Text.Json;

using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Queries;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Research;

/// <summary>The Pleb working through the curation queue the first start fills, with its real tools.</summary>
public sealed class CurationTests : IAsyncDisposable
{
    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task ThePlebCuratesATopicOnFirstStart()
    {
        _wiki.Write(Path.Combine(_wiki.Settings.WikiDirectory, "projects", "garden"), "index.md", "# Garden\n\nRaised beds and drip irrigation.\n");
        _wiki.Pleb.Behaviour = async (_, cancellationToken) =>
        {
            await _wiki.CallToolAsync(
                nameof(PlebTools.SetTopicQueries),
                null,
                () => _wiki.Get<PlebTools>().SetTopicQueries(
                    "projects/garden",
                    "News about raised-bed gardening and drip irrigation.",
                    ["drip irrigation raised beds", "raised bed soil mix"],
                    cancellationToken));
            return "Set the queries.";
        };
        await _wiki.InitialiseAsync();
        _wiki.Start();

        Result<CurationCycle, Error> cycle = await _wiki.Get<CurateTopicsCommandHandler>()
            .Handle(new CurateTopicsCommand(), CancellationToken.None);
        IReadOnlyList<AgentRun> runs = await _wiki.RunsAsync();

        ItShouldCurateTheTopic();
        ItShouldGiveThePlebTheTopicsPage();
        ItShouldStoreTheBriefAndQueries();
        ItShouldEmptyTheQueue();
        ItShouldRecordTheRun();

        void ItShouldCurateTheTopic() => cycle.Value.Curated.ShouldBe(["projects/garden"]);

        void ItShouldGiveThePlebTheTopicsPage() => _wiki.Pleb.Prompts.ShouldHaveSingleItem().ShouldContain("Raised beds and drip irrigation.");

        void ItShouldStoreTheBriefAndQueries()
        {
            ResearchTopic topic = JsonSerializer.Deserialize<ResearchTopic>(
                File.ReadAllText(Path.Combine(_wiki.Settings.ResearchTopicsDirectory, "projects--garden.json")),
                JsonSerializerOptions.Web)!;
            topic.Brief.ShouldBe("News about raised-bed gardening and drip irrigation.");
            topic.Queries.ShouldBe(["drip irrigation raised beds", "raised bed soil mix"]);
            topic.LastCuratedAt.ShouldNotBeNull();
        }

        void ItShouldEmptyTheQueue()
        {
            TestWiki.FileNames(_wiki.Settings.CurationRequestsDirectory).ShouldBeEmpty();
            TestWiki.FileNames(_wiki.Settings.CurationFailedDirectory).ShouldBeEmpty();
        }

        void ItShouldRecordTheRun() => runs.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            run => run.Agent.ShouldBe(KnownAgents.Pleb),
            run => run.Status.ShouldBe(AgentRunStatus.Succeeded),
            run => run.Trigger.Kind.ShouldBe(AgentRunTriggerKind.Curation));
    }

    [Test]
    public async Task ThePlebAsksWhatATopicIsAboutAndTheAuthorAnswers()
    {
        _wiki.Write(Path.Combine(_wiki.Settings.WikiDirectory, "learning"), "okf.md", "# OKF\n\nTrying OKF for my notes.\n");
        int runs = 0;
        _wiki.Pleb.Behaviour = async (_, cancellationToken) =>
        {
            PlebTools tools = _wiki.Get<PlebTools>();
            if (runs++ == 0)
            {
                await _wiki.CallToolAsync(
                    nameof(PlebTools.RequestHumanReview),
                    null,
                    () => tools.RequestHumanReview("learning/okf.md", "OKF could be several things; which one is meant?", cancellationToken));
                return "Asked the author.";
            }

            await _wiki.CallToolAsync(
                nameof(PlebTools.SetTopicQueries),
                null,
                () => tools.SetTopicQueries(
                    "learning/okf.md",
                    "News about the Open Knowledge Format, a file format for agent knowledge bases.",
                    ["open knowledge format release"],
                    cancellationToken));
            return "Set the queries.";
        };
        await _wiki.InitialiseAsync();
        _wiki.Start();

        Result<CurationCycle, Error> asked = await _wiki.Get<CurateTopicsCommandHandler>().Handle(new CurateTopicsCommand(), CancellationToken.None);
        TopicSummary needingReview = await TopicAsync();
        Result<SearchCycle, Error> searchedWhileAsking = await _wiki.Get<SearchTopicsCommandHandler>().Handle(new SearchTopicsCommand(), CancellationToken.None);
        Result<ResearchTopic, Error> clarified = await _wiki.Get<ClarifyTopicCommandHandler>()
            .Handle(new ClarifyTopicCommand("learning--okf.md", "The Open Knowledge Format for agent knowledge bases."), CancellationToken.None);
        TopicSummary awaitingCuration = await TopicAsync();
        Result<CurationCycle, Error> answered = await _wiki.Get<CurateTopicsCommandHandler>().Handle(new CurateTopicsCommand(), CancellationToken.None);
        TopicSummary ready = await TopicAsync();

        ItShouldEndTheFirstCurationNeedingReview();
        ItShouldNotSearchTheTopicWhileTheQuestionIsOpen();
        ItShouldQueueACurationWithTheAnswer();
        ItShouldGiveThePlebTheAnswerWithItsQuestion();
        ItShouldBeReadyOnceThePlebSetsQueries();

        void ItShouldEndTheFirstCurationNeedingReview()
        {
            asked.Value.Curated.ShouldBe(["learning/okf.md"]);
            needingReview.State.ShouldBe(TopicState.NeedsReview);
            needingReview.ReviewQuestion.ShouldBe("OKF could be several things; which one is meant?");
        }

        void ItShouldNotSearchTheTopicWhileTheQuestionIsOpen() => searchedWhileAsking.Value.Searched.ShouldBeEmpty();

        void ItShouldQueueACurationWithTheAnswer()
        {
            clarified.IsSuccess.ShouldBeTrue();
            awaitingCuration.State.ShouldBe(TopicState.AwaitingCuration);
        }

        void ItShouldGiveThePlebTheAnswerWithItsQuestion() => _wiki.Pleb.Prompts.Last().ShouldSatisfyAllConditions(
            prompt => prompt.ShouldContain("you asked: OKF could be several things; which one is meant?"),
            prompt => prompt.ShouldContain("<guidance>The Open Knowledge Format for agent knowledge bases.</guidance>"),
            prompt => prompt.ShouldNotContain("still unanswered"));

        void ItShouldBeReadyOnceThePlebSetsQueries()
        {
            answered.Value.Curated.ShouldBe(["learning/okf.md"]);
            ready.ShouldSatisfyAllConditions(
                topic => topic.State.ShouldBe(TopicState.Idle),
                topic => topic.ReviewQuestion.ShouldBeNull(),
                topic => topic.Queries.ShouldBe(["open knowledge format release"]),
                topic => topic.NextSearchAt.ShouldNotBeNull());
        }

        async Task<TopicSummary> TopicAsync() =>
            (await _wiki.Get<ListTopicsQueryHandler>().Handle(new ListTopicsQuery(), CancellationToken.None)).Value.Single();
    }

    [Test]
    public async Task ThePlebEndsWithoutAVerdict()
    {
        _wiki.Write(Path.Combine(_wiki.Settings.WikiDirectory, "learning"), "rust.md", "# Rust\n");
        await _wiki.InitialiseAsync();
        _wiki.Start();

        Result<CurationCycle, Error> cycle = await _wiki.Get<CurateTopicsCommandHandler>()
            .Handle(new CurateTopicsCommand(), CancellationToken.None);

        ItShouldFailTheCuration();
        ItShouldMoveTheRequestToTheFailedFolder();

        void ItShouldFailTheCuration() => cycle.Value.Failed.ShouldBe(["learning/rust.md"]);

        void ItShouldMoveTheRequestToTheFailedFolder()
        {
            TestWiki.FileNames(_wiki.Settings.CurationRequestsDirectory).ShouldBeEmpty();
            TestWiki.FileNames(_wiki.Settings.CurationFailedDirectory).ShouldBe(["learning--rust.md.json"]);
        }
    }
}
