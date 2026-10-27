using System.Text.Json;

using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;
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
