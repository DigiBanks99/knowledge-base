using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class MarkResultCommandHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstSearch = new(2026, 10, 20, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondSearch = new(2026, 10, 27, 7, 0, 0, TimeSpan.Zero);
    private const string Atlas = "projects/atlas";
    private const string Slug = "projects--atlas";
    private const string Discarded = "https://example.com/discarded";
    private const string Kept = "https://example.com/kept";
    private const string Unjudged = "https://example.com/unjudged";
    private const string Failed = "https://example.com/failed";
    private const string Unfetched = "https://example.com/unfetched";

    private readonly ResearchTopics _state = new();
    private readonly MarkResultCommandHandler _handler;

    public MarkResultCommandHandlerTests()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Discovered);
        _state.CompletedBatches.Add(new ResearchBatch(Atlas, FirstSearch, [Result(Discarded, Verdict.Discard("Off topic.", FirstSearch) with { Useful = 0.2 })])
        {
            Outcome = IngestionOutcome.Empty
        });
        _state.AwaitingBatches[Atlas] = new AwaitingBatch(
            new ResearchBatch(Atlas, SecondSearch,
            [
                Result(Kept, Verdict.Keep("A summary.", ["A quote."], SecondSearch) with { Useful = 0.8 }),
                Result(Unjudged, null),
                Result(Failed, Verdict.Failed("The Keeper did not answer.", SecondSearch)),
                Result(Unfetched, Verdict.Discard("The page could not be fetched: 404.", SecondSearch))
            ]),
            "research-projects--atlas.md",
            SecondSearch);
        WikiPaths wikiPaths = new(Options.Create(new WikiConfigurationSettings { RootDirectory = "/data/kb" }));
        _handler = new MarkResultCommandHandler(
            NullLogger<MarkResultCommandHandler>.Instance,
            FakeResearchStore.Holding(_state),
            wikiPaths);
    }

    [Test]
    public async Task TheAuthorMarksADiscardedResultAsUseful()
    {
        ResearchBatch before = _state.CompletedBatches.Single();

        Result<BatchResult, Error> marked = await _handler.Handle(new MarkResultCommand(Slug, FirstSearch, Discarded, ResultMark.Useful), CancellationToken.None);

        ItShouldStoreTheMarkWithTheResult();
        ItShouldLeaveTheVerdictAsItWas();
        ItShouldLeaveTheBatchWhereItWas();

        void ItShouldStoreTheMarkWithTheResult()
        {
            marked.Value.Mark.ShouldBe(ResultMark.Useful);
            _state.CompletedBatches.Single().Results.Single().Mark.ShouldBe(ResultMark.Useful);
        }

        void ItShouldLeaveTheVerdictAsItWas() => _state.CompletedBatches.Single().Results.Single().Verdict.ShouldBe(before.Results.Single().Verdict);

        void ItShouldLeaveTheBatchWhereItWas() => _state.ShouldSatisfyAllConditions(
            state => state.CompletedBatches.Single().Outcome.ShouldBe(IngestionOutcome.Empty),
            state => state.PendingBatches.ShouldBeEmpty(),
            state => state.AwaitingBatches[Atlas].Batch.Results.ShouldAllBe(result => result.Mark == null));
    }

    [Test]
    public async Task TheAuthorMarksAResultWhoseNoteIsStillBeingFiled()
    {
        Result<BatchResult, Error> marked = await _handler.Handle(new MarkResultCommand(Slug, SecondSearch, Kept, ResultMark.Noise), CancellationToken.None);

        ItShouldStoreTheMarkInTheAwaitingBatch();
        ItShouldKeepTheBatchAwaitingItsNote();

        void ItShouldStoreTheMarkInTheAwaitingBatch()
        {
            marked.IsSuccess.ShouldBeTrue();
            _state.AwaitingBatches[Atlas].Batch.Results[0].Mark.ShouldBe(ResultMark.Noise);
        }

        void ItShouldKeepTheBatchAwaitingItsNote() => _state.AwaitingBatches[Atlas].NoteFile.ShouldBe("research-projects--atlas.md");
    }

    [Test]
    public async Task TheAuthorClearsAMark()
    {
        await _handler.Handle(new MarkResultCommand(Slug, FirstSearch, Discarded, ResultMark.Noise), CancellationToken.None);

        Result<BatchResult, Error> cleared = await _handler.Handle(new MarkResultCommand(Slug, FirstSearch, Discarded, null), CancellationToken.None);

        ItShouldRemoveTheMark();

        void ItShouldRemoveTheMark()
        {
            cleared.Value.Mark.ShouldBeNull();
            _state.CompletedBatches.Single().Results.Single().Mark.ShouldBeNull();
        }
    }

    [Test]
    public async Task TheMarkIsNeitherUsefulNorNoise()
    {
        Result<BatchResult, Error> marked = await _handler.Handle(new MarkResultCommand(Slug, FirstSearch, Discarded, (ResultMark)7), CancellationToken.None);

        ItShouldBeRefused();
        ItShouldStoreNoMark();

        void ItShouldBeRefused() => marked.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("InvalidMark");

        void ItShouldStoreNoMark() => _state.CompletedBatches.Single().Results.Single().Mark.ShouldBeNull();
    }

    [Test]
    [Arguments(Unjudged)]
    [Arguments(Failed)]
    [Arguments(Unfetched)]
    public async Task TheResultHasNoVerdictFromTheKeeper(string url)
    {
        Result<BatchResult, Error> marked = await _handler.Handle(new MarkResultCommand(Slug, SecondSearch, url, ResultMark.Useful), CancellationToken.None);

        ItShouldBeRefused();
        ItShouldStoreNoMark();

        void ItShouldBeRefused() => marked.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("ResultNotJudged");

        void ItShouldStoreNoMark() => _state.AwaitingBatches[Atlas].Batch.Results.ShouldAllBe(result => result.Mark == null);
    }

    [Test]
    public async Task TheResultsBatchWasPruned()
    {
        Result<BatchResult, Error> marked = await _handler.Handle(
            new MarkResultCommand(Slug, FirstSearch.AddDays(-30), Discarded, ResultMark.Useful),
            CancellationToken.None);

        ItShouldNotFindIt();

        void ItShouldNotFindIt() => marked.Error.ShouldBeOfType<ResultNotFoundError>();
    }

    [Test]
    public async Task TheSlugLeavesTheWiki()
    {
        Result<BatchResult, Error> marked = await _handler.Handle(
            new MarkResultCommand("..--secrets", FirstSearch, Discarded, ResultMark.Useful),
            CancellationToken.None);

        ItShouldBeRefused();

        void ItShouldBeRefused() => marked.Error.ShouldBeOfType<ValidationError>();
    }

    private static BatchResult Result(string url, Verdict? verdict) =>
        new("atlas release", url, new SearchResult(url, "A page", null, "SearXNG", null), Verdict: verdict);
}
