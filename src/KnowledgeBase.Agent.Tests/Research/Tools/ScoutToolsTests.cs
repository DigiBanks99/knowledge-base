using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Tools;

public sealed class ScoutToolsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Atlas = "projects/atlas";
    private const string Url = "https://example.com/atlas-2";

    private readonly ResearchTopics _state = new();
    private readonly AgentRunTracker _runTracker;
    private readonly ScoutTools _tools;

    public ScoutToolsTests()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Now);
        _state.PendingBatches[Atlas] = new ResearchBatch(
            Atlas,
            Now,
            [new BatchResult("a query", Url, new SearchResult(Url, "Atlas 2", null, "SearXNG", null), FetchedPage.Content(Now, "Page.", false))]);
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _runTracker = new AgentRunTracker(NullLogger<AgentRunTracker>.Instance, Substitute.For<IAgentRunStore>(), [], timeProvider);
        _tools = new ScoutTools(_runTracker, FakeResearchStore.Holding(_state), timeProvider);
    }

    [Test]
    public async Task TheScoutKeepsAResultWithLongQuotes()
    {
        string longQuote = new('q', ScoutTools.MaxQuoteLength + 50);

        string answer = await InTriageRun(() => _tools.KeepResult("  Atlas 2\nships.  ", [longQuote, "second", "third", "fourth", " "], CancellationToken.None));

        ItShouldStoreTheVerdictOnTheResult();
        ItShouldCapEachQuoteInCode();
        ItShouldKeepAtMostThreeQuotes();
        ItShouldPutTheSummaryOnOneLine();
        ItShouldTellTheScoutItIsDone();

        void ItShouldStoreTheVerdictOnTheResult() => Verdict.Kept.ShouldBeTrue();

        void ItShouldCapEachQuoteInCode() => Verdict.Quotes[0].Length.ShouldBe(ScoutTools.MaxQuoteLength);

        void ItShouldKeepAtMostThreeQuotes() => Verdict.Quotes.ShouldBe([Verdict.Quotes[0], "second", "third"]);

        void ItShouldPutTheSummaryOnOneLine() => Verdict.Summary.ShouldBe("Atlas 2 ships.");

        void ItShouldTellTheScoutItIsDone() => answer.ShouldContain("You are done");
    }

    [Test]
    public async Task TheScoutDiscardsAResult()
    {
        await InTriageRun(() => _tools.DiscardResult("A beginner question.", CancellationToken.None));

        ItShouldStoreTheDiscardWithItsReason();

        void ItShouldStoreTheDiscardWithItsReason() => Verdict.ShouldSatisfyAllConditions(
            verdict => verdict.Kept.ShouldBeFalse(),
            verdict => verdict.Reason.ShouldBe("A beginner question."));
    }

    [Test]
    public async Task TheScoutKeepsWithoutASummary()
    {
        Exception? error = await Catch.ExceptionAsync(() => InTriageRun(() => _tools.KeepResult("  ", [], CancellationToken.None)));

        ItShouldTellTheScoutHowToCorrectTheCall();
        ItShouldStoreNoVerdict();

        void ItShouldTellTheScoutHowToCorrectTheCall() => error.ShouldBeOfType<InvalidOperationException>().Message.ShouldStartWith("The summary is empty. Call `KeepResult` again");

        void ItShouldStoreNoVerdict() => _state.PendingBatches[Atlas].Results.Single().Verdict.ShouldBeNull();
    }

    [Test]
    public async Task TheScoutGivesASecondVerdict()
    {
        Exception? error = await Catch.ExceptionAsync(() => InTriageRun(async () =>
        {
            await _tools.KeepResult("Useful.", [], CancellationToken.None);
            return await _tools.DiscardResult("Noise.", CancellationToken.None);
        }));

        ItShouldRefuseIt();
        ItShouldKeepTheFirstVerdict();

        void ItShouldRefuseIt() => error.ShouldBeOfType<InvalidOperationException>().Message.ShouldStartWith("You have already given a verdict");

        void ItShouldKeepTheFirstVerdict() => Verdict.Kept.ShouldBeTrue();
    }

    [Test]
    public async Task AToolIsCalledOutsideATriageRun()
    {
        Exception? error = await Catch.ExceptionAsync(() => _tools.DiscardResult("Noise.", CancellationToken.None));

        ItShouldBeRefused();

        void ItShouldBeRefused() => error.ShouldBeOfType<InvalidOperationException>().Message.ShouldStartWith("There is no result to judge");
    }

    private ScoutVerdict Verdict => _state.PendingBatches[Atlas].Results.Single().Verdict.ShouldNotBeNull();

    private Task<string> InTriageRun(Func<Task<string>> call) =>
        _runTracker.RunAsync(
                KnownAgents.Scout,
                new AgentRunTrigger(AgentRunTriggerKind.Triage, Url),
                KnowledgeBase.Agent.Facts.Models.FactOrigin.Research,
                Atlas,
                async _ => new Microsoft.Agents.AI.AgentResponse(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, await call())),
                CancellationToken.None)
            .ContinueWith(
                run => run.Result.IsSuccess
                    ? run.Result.Value.Summary!
                    : throw new InvalidOperationException(((AgentRunFailedError)run.Result.Error).Run.Error),
                TaskScheduler.Default);
}
