using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Tools;

public sealed class ClerkToolsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Atlas = "projects/atlas";
    private const string Kept = "https://example.com/atlas-2";
    private const string Discarded = "https://example.com/rant";

    private readonly ResearchTopics _state = new();
    private readonly AgentRunTracker _runTracker;
    private readonly ClerkTools _tools;

    public ClerkToolsTests()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Now);
        _state.PendingBatches[Atlas] = new ResearchBatch(
            Atlas,
            Now,
            [
                Result(Kept, Verdict.Keep(null, ["A quote."], Now)),
                Result(Discarded, Verdict.Discard("Noise.", Now))
            ]);
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _runTracker = new AgentRunTracker(NullLogger<AgentRunTracker>.Instance, Substitute.For<IAgentRunStore>(), [], timeProvider);
        _tools = new ClerkTools(_runTracker, FakeResearchStore.Holding(_state));
    }

    [Test]
    public async Task TheClerkRecordsALongMultiLineSummary()
    {
        string summary = $"  Atlas 2\nships.\n\n# {new string('s', ClerkTools.MaxSummaryLength)}  ";

        string answer = await InClerkRun(Kept, () => _tools.RecordSummary(summary, CancellationToken.None));

        ItShouldStoreTheSummaryOnTheResult();
        ItShouldPutItOnOneLine();
        ItShouldCapItInCode();
        ItShouldKeepTheQuotes();
        ItShouldTellTheClerkItIsDone();

        void ItShouldStoreTheSummaryOnTheResult() => VerdictOf(Kept).Summary.ShouldNotBeNull();

        void ItShouldPutItOnOneLine() => VerdictOf(Kept).Summary!.ShouldStartWith("Atlas 2 ships. # sss");

        void ItShouldCapItInCode() => VerdictOf(Kept).Summary!.Length.ShouldBe(ClerkTools.MaxSummaryLength);

        void ItShouldKeepTheQuotes() => VerdictOf(Kept).Quotes.ShouldBe(["A quote."]);

        void ItShouldTellTheClerkItIsDone() => answer.ShouldContain("You are done");
    }

    [Test]
    public async Task TheClerkRecordsAnEmptySummary()
    {
        Exception? error = await Catch.ExceptionAsync(() => InClerkRun(Kept, () => _tools.RecordSummary(" \n ", CancellationToken.None)));

        ItShouldTellTheClerkHowToCorrectTheCall();
        ItShouldStoreNoSummary();

        void ItShouldTellTheClerkHowToCorrectTheCall() =>
            error.ShouldBeOfType<InvalidOperationException>().Message.ShouldStartWith("The summary is empty. Call `RecordSummary` again");

        void ItShouldStoreNoSummary() => VerdictOf(Kept).Summary.ShouldBeNull();
    }

    [Test]
    public async Task TheClerkRecordsASecondSummary()
    {
        Exception? error = await Catch.ExceptionAsync(() => InClerkRun(Kept, async () =>
        {
            await _tools.RecordSummary("The first.", CancellationToken.None);
            return await _tools.RecordSummary("The second.", CancellationToken.None);
        }));

        ItShouldRefuseIt();
        ItShouldKeepTheFirstSummary();

        void ItShouldRefuseIt() =>
            error.ShouldBeOfType<InvalidOperationException>().Message.ShouldStartWith("You have already recorded the summary.");

        void ItShouldKeepTheFirstSummary() => VerdictOf(Kept).Summary.ShouldBe("The first.");
    }

    [Test]
    public async Task TheClerkSummarisesADiscardedResult()
    {
        Exception? error = await Catch.ExceptionAsync(() => InClerkRun(Discarded, () => _tools.RecordSummary("A summary.", CancellationToken.None)));

        ItShouldBeRefused();
        ItShouldStoreNoSummary();

        void ItShouldBeRefused() =>
            error.ShouldBeOfType<InvalidOperationException>().Message.ShouldStartWith("The result you were given is no longer waiting for a summary.");

        void ItShouldStoreNoSummary() => VerdictOf(Discarded).Summary.ShouldBeNull();
    }

    [Test]
    public async Task TheToolIsCalledOutsideAClerkRun()
    {
        Exception? error = await Catch.ExceptionAsync(() => _tools.RecordSummary("A summary.", CancellationToken.None));

        ItShouldBeRefused();

        void ItShouldBeRefused() =>
            error.ShouldBeOfType<InvalidOperationException>().Message.ShouldStartWith("There is no result to summarise");
    }

    private Verdict VerdictOf(string url) =>
        _state.PendingBatches[Atlas].Results.Single(result => result.Url == url).Verdict.ShouldNotBeNull();

    private static BatchResult Result(string url, Verdict verdict) =>
        new("a query", url, new SearchResult(url, "A title", null, "SearXNG", null), FetchedPage.Content(Now, "Page.", false), verdict);

    private Task<string> InClerkRun(string url, Func<Task<string>> call) =>
        _runTracker.RunAsync(
                KnownAgents.Clerk,
                new AgentRunTrigger(AgentRunTriggerKind.Triage, url),
                FactOrigin.Research,
                Atlas,
                async _ => new AgentResponse(new ChatMessage(ChatRole.Assistant, await call())),
                CancellationToken.None)
            .ContinueWith(
                run => run.Result.IsSuccess
                    ? run.Result.Value.Summary!
                    : throw new InvalidOperationException(((AgentRunFailedError)run.Result.Error).Run.Error),
                TaskScheduler.Default);
}
