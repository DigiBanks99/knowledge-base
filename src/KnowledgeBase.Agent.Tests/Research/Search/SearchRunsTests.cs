using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Tests.Research.Search;

public sealed class SearchRunsTests
{
    private const string Atlas = "projects/atlas";
    private const string RunId = "20261028-120000-search-0123456789abcdef";

    private static readonly DateTimeOffset Started = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Completed = Started.AddSeconds(3);

    [Test]
    public void EverySourceAnswers()
    {
        SourceCall[] calls =
        [
            Answered("atlas release", "SearXNG", 3),
            Answered("atlas release", "Hacker News", 1)
        ];

        AgentRun run = SearchRuns.Searched(RunId, Atlas, SearchStart.Scheduled, Started, Completed, calls, Filtered(kept: 2, blocked: 1, alreadySeen: 1, tooOld: 0));

        ItShouldBeASearchOfTheTopic();
        ItShouldListEachQueryAndSourceWithItsCount();
        ItShouldSummariseWhatTheFiltersMadeOfIt();

        void ItShouldBeASearchOfTheTopic() => run.ShouldSatisfyAllConditions(
            () => run.Id.ShouldBe(RunId),
            () => run.Agent.ShouldBe("Search"),
            () => run.Trigger.ShouldBe(new AgentRunTrigger(AgentRunTriggerKind.Search, "Scheduled")),
            () => run.Status.ShouldBe(AgentRunStatus.Succeeded),
            () => run.StartedAt.ShouldBe(Started),
            () => run.CompletedAt.ShouldBe(Completed),
            () => run.Origin.ShouldBe(FactOrigin.Research),
            () => run.Topic.ShouldBe(Atlas));

        void ItShouldListEachQueryAndSourceWithItsCount() => run.ToolCalls
            .Select(call => (call.Tool, call.Target, call.Succeeded, call.Detail))
            .ShouldBe(
            [
                ("Search", "atlas release", true, "SearXNG: 3 results"),
                ("Search", "atlas release", true, "Hacker News: 1 result")
            ]);

        void ItShouldSummariseWhatTheFiltersMadeOfIt() =>
            run.Summary.ShouldBe("Found 4 results: 1 blocked, 1 already seen, 0 too old, 2 kept. Wrote a batch of 2 for triage.");
    }

    [Test]
    public void OneSourceFails()
    {
        SourceCall[] calls =
        [
            Answered("atlas release", "SearXNG", 2),
            Failed("atlas release", "Hacker News", "unreachable.")
        ];

        AgentRun run = SearchRuns.Searched(RunId, Atlas, SearchStart.ResearchNow, Started, Completed, calls, Filtered(kept: 2));

        ItShouldStillSucceed();
        ItShouldRecordTheFailedCallWithItsError();
        ItShouldSayTheSearchWasIncomplete();

        void ItShouldStillSucceed() => run.Status.ShouldBe(AgentRunStatus.Succeeded);

        void ItShouldRecordTheFailedCallWithItsError() => run.ToolCalls[1].ShouldSatisfyAllConditions(
            call => call.Succeeded.ShouldBeFalse(),
            call => call.Error.ShouldBe("Hacker News could not be searched: unreachable."),
            call => call.Detail.ShouldBeNull());

        void ItShouldSayTheSearchWasIncomplete() => run.Summary.ShouldBe(
            "Found 2 results: 0 blocked, 0 already seen, 0 too old, 2 kept. Wrote a batch of 2 for triage. 1 of 2 source calls failed; the next search asks again for everything since the last complete one.");
    }

    [Test]
    public void NothingIsKept()
    {
        AgentRun run = SearchRuns.Searched(RunId, Atlas, SearchStart.Scheduled, Started, Completed, [Answered("atlas release", "SearXNG", 1)], Filtered(kept: 0, alreadySeen: 1));

        ItShouldSayNoBatchWasWritten();

        void ItShouldSayNoBatchWasWritten() =>
            run.Summary.ShouldBe("Found 1 result: 0 blocked, 1 already seen, 0 too old, 0 kept. Kept nothing, so no batch was written.");
    }

    [Test]
    public void EverySourceFails()
    {
        SearchSourceError error = new("SearXNG", "unreachable.");
        SourceCall[] calls = [Failed("atlas release", "SearXNG", "unreachable.")];

        AgentRun run = SearchRuns.Failed(RunId, Atlas, SearchStart.Scheduled, Started, Completed, calls, null, false, error);

        ItShouldBeRecordedAsFailedWithTheError();

        void ItShouldBeRecordedAsFailedWithTheError() => run.ShouldSatisfyAllConditions(
            () => run.Status.ShouldBe(AgentRunStatus.Failed),
            () => run.Error.ShouldBe(error.Message),
            () => run.Summary.ShouldBeNull(),
            () => run.ToolCalls.ShouldHaveSingleItem().Succeeded.ShouldBeFalse());
    }

    [Test]
    public void TheTopicCannotBeMarkedSearchedAfterItsBatchWasWritten()
    {
        ResearchStoreError error = new("projects--atlas.json", "The disk is full.");

        AgentRun run = SearchRuns.Failed(RunId, Atlas, SearchStart.Scheduled, Started, Completed, [Answered("atlas release", "SearXNG", 2)], Filtered(kept: 2), true, error);

        ItShouldSayTheBatchWasStillWritten();

        void ItShouldSayTheBatchWasStillWritten() => run.ShouldSatisfyAllConditions(
            () => run.Status.ShouldBe(AgentRunStatus.Failed),
            () => run.Summary.ShouldBe("Found 2 results: 0 blocked, 0 already seen, 0 too old, 2 kept. Wrote a batch of 2 for triage."));
    }

    [Test]
    public void TheBatchCannotBeWritten()
    {
        ResearchStoreError error = new("projects--atlas.json", "The disk is full.");

        AgentRun run = SearchRuns.Failed(RunId, Atlas, SearchStart.Scheduled, Started, Completed, [Answered("atlas release", "SearXNG", 2)], Filtered(kept: 2), false, error);

        ItShouldSayNoBatchWasWritten();

        void ItShouldSayNoBatchWasWritten() =>
            run.Summary.ShouldBe("Found 2 results: 0 blocked, 0 already seen, 0 too old, 2 kept. No batch was written.");
    }

    [Test]
    public void OneSourceAnswersSlowly()
    {
        DateTimeOffset slow = Started.AddSeconds(30);

        AgentRun run = SearchRuns.Running(RunId, Atlas, SearchStart.Scheduled, Started, [Answered("atlas release", "SearXNG", 1, slow), Answered("atlas release", "Hacker News", 1)]);

        ItShouldStampEachLineWithWhenItsSourceAnswered();

        void ItShouldStampEachLineWithWhenItsSourceAnswered() => run.ToolCalls.Select(call => call.At).ShouldBe([slow, Started]);
    }

    [Test]
    public void ABatchsPagesAreFetchedAndJudged()
    {
        BatchResult[] results =
        [
            Found("https://example.com/a", FetchedPage.Content(Started, "A", false), Verdict.Keep("A summary.", [], Completed)),
            Found("https://example.com/b", FetchedPage.Failed(Started, "Refused."), Verdict.Discard("The page could not be fetched.", Completed)),
            Found("https://example.com/c", FetchedPage.Content(Started, "C", false), Verdict.Failed("The Scout ended without a verdict.", Completed)),
            Found("https://example.com/d", null, null)
        ];

        IReadOnlyList<AgentToolCall> fetched = SearchRuns.Fetched(results);
        IReadOnlyList<AgentToolCall> judged = SearchRuns.Judged(results);

        ItShouldListEachFetchedPage();
        ItShouldListEachVerdict();

        void ItShouldListEachFetchedPage() => fetched
            .Select(call => (call.Tool, call.Target, call.Succeeded, call.Error))
            .ShouldBe(
            [
                ("Fetch", "https://example.com/a", true, null),
                ("Fetch", "https://example.com/b", false, "Refused."),
                ("Fetch", "https://example.com/c", true, null)
            ]);

        void ItShouldListEachVerdict() => judged
            .Select(call => (call.Tool, call.Target, call.Succeeded, call.Error, call.Detail))
            .ShouldBe(
            [
                ("Keep", "https://example.com/a", true, null, null),
                ("Discard", "https://example.com/b", true, null, "The page could not be fetched."),
                ("Discard", "https://example.com/c", false, "The Scout ended without a verdict.", null)
            ]);
    }

    private static SourceCall Answered(string query, string source, int count, DateTimeOffset? answeredAt = null) => new(
        query,
        source,
        Result.Success<IReadOnlyList<SearchResult>, Error>(
            [.. Enumerable.Range(1, count).Select(index => new SearchResult($"https://example.com/{source}/{index}", "A result", null, source, null))]),
        answeredAt ?? Started);

    private static SourceCall Failed(string query, string source, string reason) =>
        new(query, source, Result.Failure<IReadOnlyList<SearchResult>, Error>(new SearchSourceError(source, reason)), Started);

    private static FilteredResults Filtered(int kept, int blocked = 0, int alreadySeen = 0, int tooOld = 0) => new(
        [.. Enumerable.Range(1, kept).Select(index => Found($"https://example.com/kept/{index}", null, null))],
        [],
        blocked,
        alreadySeen,
        tooOld);

    private static BatchResult Found(string url, FetchedPage? page, Verdict? verdict) =>
        new("atlas release", url, new SearchResult(url, "A result", null, "SearXNG", null), page, verdict);
}
