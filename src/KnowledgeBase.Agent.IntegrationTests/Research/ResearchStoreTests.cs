using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.IntegrationTests.Research;

public sealed class ResearchStoreTests : IDisposable
{
    private static readonly DenyListEntry JaneDoe = new("Jane Doe", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied);
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _root = new();
    private readonly WikiConfigurationSettings _settings;
    private readonly ResearchStore _store;

    public ResearchStoreTests()
    {
        _settings = new WikiConfigurationSettings { RootDirectory = _root.Path };
        _store = new ResearchStore(Options.Create(_settings));
    }

    public void Dispose()
    {
        _store.Dispose();
        _root.Dispose();
    }

    [Test]
    public async Task NothingHasBeenDeniedYet()
    {
        Result<DenyList, Error> denyList = await _store.ReadDenyListAsync(CancellationToken.None);

        ItShouldReadAnEmptyDenyList();

        void ItShouldReadAnEmptyDenyList() => denyList.Value.Entries.ShouldBeEmpty();
    }

    [Test]
    public async Task TheDenyListIsUpdated()
    {
        await _store.UpdateDenyListAsync(denyList => denyList.WithAutomatic(JaneDoe.Name, JaneDoe.Kind), CancellationToken.None);
        Result<DenyList, Error> read = await new ResearchStore(Options.Create(_settings)).ReadDenyListAsync(CancellationToken.None);

        ItShouldPersistItUnderTheResearchDirectory();
        ItShouldReadItBack();

        void ItShouldPersistItUnderTheResearchDirectory() =>
            TestWiki.FileNames(_settings.ResearchDirectory).ShouldBe(["denylist.json"]);

        void ItShouldReadItBack() => read.Value.Entries.ShouldBe([JaneDoe]);
    }

    [Test]
    public async Task ManyUpdatesArriveAtOnce()
    {
        await Task.WhenAll(Enumerable.Range(1, 20).Select(index => _store.UpdateDenyListAsync(
            denyList => denyList.WithAutomatic($"Person {index}", SubjectKind.Person),
            CancellationToken.None)));
        Result<DenyList, Error> read = await _store.ReadDenyListAsync(CancellationToken.None);

        ItShouldKeepEveryUpdate();

        void ItShouldKeepEveryUpdate() => read.Value.Entries.Count.ShouldBe(20);
    }

    [Test]
    public async Task AnUpdateFails()
    {
        await _store.UpdateDenyListAsync(denyList => denyList.WithAutomatic(JaneDoe.Name, JaneDoe.Kind), CancellationToken.None);

        Result<DenyList, Error> result = await _store.UpdateDenyListAsync(
            denyList => denyList.WithManual("Unknown", DenyListState.Allowed),
            CancellationToken.None);
        Result<DenyList, Error> read = await _store.ReadDenyListAsync(CancellationToken.None);

        ItShouldReturnTheFailure();
        ItShouldKeepTheDenyListAsItWas();

        void ItShouldReturnTheFailure() => result.Error.ShouldBeOfType<DenyListEntryNotFoundError>();

        void ItShouldKeepTheDenyListAsItWas() => read.Value.Entries.ShouldBe([JaneDoe]);
    }

    [Test]
    public async Task ATopicIsDiscovered()
    {
        ResearchTopic topic = ResearchTopic.New("projects/garden", Discovered);

        await _store.UpdateTopicAsync(topic.Path, _ => Maybe.From(topic), CancellationToken.None);
        Result<IReadOnlyList<ResearchTopic>, Error> read = await new ResearchStore(Options.Create(_settings)).ListTopicsAsync(CancellationToken.None);

        ItShouldKeepItUnderItsSlug();
        ItShouldReadItBack();

        void ItShouldKeepItUnderItsSlug() =>
            TestWiki.FileNames(_settings.ResearchTopicsDirectory).ShouldBe(["projects--garden.json"]);

        void ItShouldReadItBack() => read.Value.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            stored => stored.Path.ShouldBe(topic.Path),
            stored => stored.Queries.ShouldBeEmpty(),
            stored => stored.CreatedAt.ShouldBe(Discovered));
    }

    [Test]
    public async Task CurationIsRequestedTwiceForATopic()
    {
        await _store.RequestCurationAsync(new CurationRequest("projects/garden", ["projects/garden/a.md"], Discovered), CancellationToken.None);
        await _store.RequestCurationAsync(new CurationRequest("projects/garden", ["projects/garden/b.md"], Discovered.AddHours(1)), CancellationToken.None);
        Result<IReadOnlyList<CurationRequest>, Error> read = await _store.ListCurationRequestsAsync(CancellationToken.None);

        ItShouldKeepOneRequestFile();
        ItShouldMergeTheChanges();

        void ItShouldKeepOneRequestFile() =>
            TestWiki.FileNames(_settings.CurationRequestsDirectory).ShouldBe(["projects--garden.json"]);

        void ItShouldMergeTheChanges() => read.Value.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            request => request.ChangedPaths.ShouldBe(["projects/garden/a.md", "projects/garden/b.md"]),
            request => request.RequestedAt.ShouldBe(Discovered.AddHours(1)));
    }

    [Test]
    public async Task ATopicIsPruned()
    {
        await _store.UpdateTopicAsync("projects/garden", _ => Maybe.From(ResearchTopic.New("projects/garden", Discovered)), CancellationToken.None);
        await _store.RequestCurationAsync(new CurationRequest("projects/garden", [], Discovered), CancellationToken.None);

        Result<Maybe<ResearchTopic>, Error> result = await _store.UpdateTopicAsync("projects/garden", _ => Maybe<ResearchTopic>.None, CancellationToken.None);

        ItShouldSucceed();
        ItShouldRemoveItsState();
        ItShouldRemoveItsWaitingRequest();

        void ItShouldSucceed() => result.IsSuccess.ShouldBeTrue();

        void ItShouldRemoveItsState() => TestWiki.FileNames(_settings.ResearchTopicsDirectory).ShouldBeEmpty();

        void ItShouldRemoveItsWaitingRequest() => TestWiki.FileNames(_settings.CurationRequestsDirectory).ShouldBeEmpty();
    }

    [Test]
    public async Task ACurationIsCompleted()
    {
        CurationRequest request = await RequestCurationAsync("projects/garden", "projects/garden/index.md");
        await _store.FailCurationAsync(new FailedCuration(request, "No verdict.", Discovered), CancellationToken.None);
        CurationRequest again = await RequestCurationAsync("projects/garden", "projects/garden/beds.md");

        UnitResult<Error> result = await _store.CompleteCurationAsync(again, CancellationToken.None);

        ItShouldSucceed();
        ItShouldRemoveTheRequest();
        ItShouldClearTheEarlierFailure();

        void ItShouldSucceed() => result.IsSuccess.ShouldBeTrue();

        void ItShouldRemoveTheRequest() => TestWiki.FileNames(_settings.CurationRequestsDirectory).ShouldBeEmpty();

        void ItShouldClearTheEarlierFailure() => TestWiki.FileNames(_settings.CurationFailedDirectory).ShouldBeEmpty();
    }

    [Test]
    public async Task ACurationFails()
    {
        CurationRequest request = await RequestCurationAsync("projects/garden", "projects/garden/index.md");

        UnitResult<Error> result = await _store.FailCurationAsync(new FailedCuration(request, "No verdict.", Discovered), CancellationToken.None);
        Result<IReadOnlyList<FailedCuration>, Error> failed = await _store.ListFailedCurationsAsync(CancellationToken.None);

        ItShouldSucceed();
        ItShouldMoveTheRequestToTheFailedFolder();
        ItShouldListTheFailureWithItsReason();

        void ItShouldSucceed() => result.IsSuccess.ShouldBeTrue();

        void ItShouldMoveTheRequestToTheFailedFolder()
        {
            TestWiki.FileNames(_settings.CurationRequestsDirectory).ShouldBeEmpty();
            TestWiki.FileNames(_settings.CurationFailedDirectory).ShouldBe(["projects--garden.json"]);
        }

        void ItShouldListTheFailureWithItsReason() => failed.Value.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            failure => failure.Reason.ShouldBe("No verdict."),
            failure => failure.Request.ChangedPaths.ShouldBe(["projects/garden/index.md"]));
    }

    [Test]
    public async Task ATopicChangesWhileItIsCurated()
    {
        CurationRequest curated = await RequestCurationAsync("projects/garden", "projects/garden/index.md");
        await RequestCurationAsync("projects/garden", "projects/garden/beds.md", minutesLater: 5);

        await _store.CompleteCurationAsync(curated, CancellationToken.None);
        Result<IReadOnlyList<CurationRequest>, Error> waiting = await _store.ListCurationRequestsAsync(CancellationToken.None);

        ItShouldKeepTheNewerRequestWaiting();

        void ItShouldKeepTheNewerRequestWaiting() => waiting.Value.ShouldHaveSingleItem().ChangedPaths
            .ShouldBe(["projects/garden/index.md", "projects/garden/beds.md"]);
    }

    [Test]
    public async Task ATopicWithAFailedCurationIsPruned()
    {
        CurationRequest request = await RequestCurationAsync("projects/garden", "projects/garden/index.md");
        await _store.FailCurationAsync(new FailedCuration(request, "No verdict.", Discovered), CancellationToken.None);

        await _store.UpdateTopicAsync("projects/garden", _ => Maybe<ResearchTopic>.None, CancellationToken.None);

        ItShouldRemoveTheFailure();

        void ItShouldRemoveTheFailure() => TestWiki.FileNames(_settings.CurationFailedDirectory).ShouldBeEmpty();
    }

    private async Task<CurationRequest> RequestCurationAsync(string topic, string changedPath, int minutesLater = 0)
    {
        await _store.UpdateTopicAsync(topic, current => current.HasValue ? current : Maybe.From(ResearchTopic.New(topic, Discovered)), CancellationToken.None);
        Result<CurationRequest, Error> requested = await _store.RequestCurationAsync(
            new CurationRequest(topic, [changedPath], Discovered.AddMinutes(minutesLater)),
            CancellationToken.None);
        return requested.Value;
    }

    [Test]
    public async Task ATopicsStateIsCorrupt()
    {
        Directory.CreateDirectory(_settings.ResearchTopicsDirectory);
        string topicPath = Path.Combine(_settings.ResearchTopicsDirectory, "projects--garden.json");
        await File.WriteAllTextAsync(topicPath, "{ not json");

        Result<Maybe<ResearchTopic>, Error> updated = await _store.UpdateTopicAsync(
            "projects/garden",
            _ => Maybe.From(ResearchTopic.New("projects/garden", Discovered)),
            CancellationToken.None);
        Result<IReadOnlyList<ResearchTopic>, Error> listed = await _store.ListTopicsAsync(CancellationToken.None);

        ItShouldRefuseToOverwriteIt();
        ItShouldSkipItInTheListing();

        void ItShouldRefuseToOverwriteIt()
        {
            updated.Error.ShouldBeOfType<ResearchStoreError>();
            File.ReadAllText(topicPath).ShouldBe("{ not json");
        }

        void ItShouldSkipItInTheListing() => listed.Value.ShouldBeEmpty();
    }

    [Test]
    [Arguments("{ not json")]
    [Arguments("{\"seededFromPeople\":true}")]
    [Arguments("{\"entries\":null}")]
    public async Task TheDenyListIsCorrupt(string content)
    {
        Directory.CreateDirectory(_settings.ResearchDirectory);
        await File.WriteAllTextAsync(_settings.DenyListPath, content);

        Result<DenyList, Error> read = await _store.ReadDenyListAsync(CancellationToken.None);
        Result<DenyList, Error> updated = await _store.UpdateDenyListAsync(
            denyList => denyList.WithAutomatic(JaneDoe.Name, JaneDoe.Kind),
            CancellationToken.None);

        ItShouldRefuseToReadIt();
        ItShouldNotOverwriteIt();

        void ItShouldRefuseToReadIt() => read.Error.ShouldBeOfType<ResearchStoreError>();

        void ItShouldNotOverwriteIt()
        {
            updated.Error.ShouldBeOfType<ResearchStoreError>();
            File.ReadAllText(_settings.DenyListPath).ShouldBe(content);
        }
    }

    [Test]
    public async Task ASearchKeepsResults()
    {
        await _store.UpdateTopicAsync("projects/garden", _ => Maybe.From(ResearchTopic.New("projects/garden", Discovered)), CancellationToken.None);
        ResearchBatch batch = Batch("projects/garden");

        Result<Maybe<ResearchBatch>, Error> written = await _store.WritePendingBatchAsync(batch, CancellationToken.None);
        ResearchStore restarted = new(Options.Create(_settings));
        Result<IReadOnlyList<ResearchBatch>, Error> listed = await restarted.ListPendingBatchesAsync(CancellationToken.None);
        Result<Maybe<ResearchBatch>, Error> read = await restarted.ReadPendingBatchAsync("projects/garden", CancellationToken.None);

        ItShouldPersistItUnderThePendingBatches();
        ItShouldReadItBack();

        void ItShouldPersistItUnderThePendingBatches()
        {
            written.Value.HasValue.ShouldBeTrue();
            TestWiki.FileNames(_settings.PendingBatchesDirectory).ShouldBe(["projects--garden.json"]);
        }

        void ItShouldReadItBack()
        {
            listed.Value.ShouldHaveSingleItem().Results.ShouldBe(batch.Results);
            read.Value.Value.Results.ShouldBe(batch.Results);
        }
    }

    [Test]
    public async Task APendingResultIsFetched()
    {
        await _store.UpdateTopicAsync("projects/garden", _ => Maybe.From(ResearchTopic.New("projects/garden", Discovered)), CancellationToken.None);
        await _store.WritePendingBatchAsync(Batch("projects/garden"), CancellationToken.None);
        FetchedPage page = FetchedPage.Content(Discovered, "# Raised beds", truncated: false);

        Result<Maybe<ResearchBatch>, Error> updated = await _store.UpdatePendingBatchAsync(
            "projects/garden",
            batch => batch with { Results = [.. batch.Results.Select(result => result with { Page = page })] },
            CancellationToken.None);
        Result<Maybe<ResearchBatch>, Error> read = await new ResearchStore(Options.Create(_settings))
            .ReadPendingBatchAsync("projects/garden", CancellationToken.None);

        ItShouldKeepThePageWithItsResult();

        void ItShouldKeepThePageWithItsResult()
        {
            updated.Value.Value.Results.ShouldAllBe(result => result.Page == page);
            read.Value.Value.Results.ShouldAllBe(result => result.Page == page);
        }
    }

    [Test]
    public async Task AResultIsFetchedForABatchThatIsGone()
    {
        Result<Maybe<ResearchBatch>, Error> updated = await _store.UpdatePendingBatchAsync(
            "projects/garden",
            batch => batch,
            CancellationToken.None);

        ItShouldWriteNoBatch();

        void ItShouldWriteNoBatch()
        {
            updated.Value.HasNoValue.ShouldBeTrue();
            Directory.Exists(_settings.PendingBatchesDirectory).ShouldBeFalse();
        }
    }

    [Test]
    public async Task APendingBatchIsAwaitingIngestion()
    {
        await _store.UpdateTopicAsync("projects/garden", _ => Maybe.From(ResearchTopic.New("projects/garden", Discovered)), CancellationToken.None);
        await _store.WritePendingBatchAsync(Batch("projects/garden"), CancellationToken.None);

        Result<Maybe<AwaitingBatch>, Error> awaiting = await _store.AwaitBatchAsync("projects/garden", "research-projects--garden.md", Discovered, CancellationToken.None);
        ResearchStore restarted = new(Options.Create(_settings));
        Result<IReadOnlySet<string>, Error> inFlight = await restarted.ListTopicsInFlightAsync(CancellationToken.None);
        Result<Maybe<ResearchBatch>, Error> pending = await restarted.ReadPendingBatchAsync("projects/garden", CancellationToken.None);

        ItShouldMoveTheBatchToTheAwaitingBatches();
        ItShouldKeepTheTopicInFlight();
        ItShouldNoLongerBePending();

        void ItShouldMoveTheBatchToTheAwaitingBatches()
        {
            awaiting.Value.Value.NoteFile.ShouldBe("research-projects--garden.md");
            TestWiki.FileNames(_settings.AwaitingBatchesDirectory).ShouldBe(["projects--garden.json"]);
            TestWiki.FileNames(_settings.PendingBatchesDirectory).ShouldBeEmpty();
        }

        void ItShouldKeepTheTopicInFlight() => inFlight.Value.ShouldBe(["projects/garden"]);

        void ItShouldNoLongerBePending() => pending.Value.HasNoValue.ShouldBeTrue();
    }

    [Test]
    public async Task APendingBatchHasNothingKept()
    {
        await _store.UpdateTopicAsync("projects/garden", _ => Maybe.From(ResearchTopic.New("projects/garden", Discovered)), CancellationToken.None);
        await _store.WritePendingBatchAsync(Batch("projects/garden"), CancellationToken.None);

        Result<Maybe<ResearchBatch>, Error> completed = await _store.CompleteBatchAsync("projects/garden", CancellationToken.None);
        Result<IReadOnlySet<string>, Error> inFlight = await _store.ListTopicsInFlightAsync(CancellationToken.None);

        ItShouldMoveTheBatchToTheCompletedBatches();
        ItShouldFreeTheTopic();

        void ItShouldMoveTheBatchToTheCompletedBatches()
        {
            completed.Value.HasValue.ShouldBeTrue();
            TestWiki.FileNames(_settings.CompletedBatchesDirectory).ShouldBe(["projects--garden-20261025-093000.json"]);
            TestWiki.FileNames(_settings.PendingBatchesDirectory).ShouldBeEmpty();
        }

        void ItShouldFreeTheTopic() => inFlight.Value.ShouldBeEmpty();
    }

    [Test]
    public async Task ABatchIsMovedThatIsGone()
    {
        Result<Maybe<AwaitingBatch>, Error> awaiting = await _store.AwaitBatchAsync("projects/garden", "note.md", Discovered, CancellationToken.None);
        Result<Maybe<ResearchBatch>, Error> completed = await _store.CompleteBatchAsync("projects/garden", CancellationToken.None);

        ItShouldMoveNothing();

        void ItShouldMoveNothing()
        {
            awaiting.Value.HasNoValue.ShouldBeTrue();
            completed.Value.HasNoValue.ShouldBeTrue();
            Directory.Exists(_settings.AwaitingBatchesDirectory).ShouldBeFalse();
        }
    }

    [Test]
    public async Task APendingBatchIsCorrupt()
    {
        Directory.CreateDirectory(_settings.PendingBatchesDirectory);
        await File.WriteAllTextAsync(Path.Combine(_settings.PendingBatchesDirectory, "projects--garden.json"), "{ not json");

        Result<IReadOnlySet<string>, Error> inFlight = await _store.ListTopicsInFlightAsync(CancellationToken.None);
        Result<IReadOnlyList<ResearchBatch>, Error> listed = await _store.ListPendingBatchesAsync(CancellationToken.None);

        ItShouldStillCountItsTopicAsInFlight();
        ItShouldSkipItInTheListing();

        // Otherwise the topic would look due, and every cycle would pick it first and fail to search it.
        void ItShouldStillCountItsTopicAsInFlight() => inFlight.Value.ShouldBe(["projects/garden"]);

        void ItShouldSkipItInTheListing() => listed.Value.ShouldBeEmpty();
    }

    [Test]
    public async Task ATopicIsPrunedWhileItIsSearched()
    {
        Result<Maybe<ResearchBatch>, Error> written = await _store.WritePendingBatchAsync(Batch("projects/garden"), CancellationToken.None);

        ItShouldWriteNoBatch();

        void ItShouldWriteNoBatch()
        {
            written.Value.HasNoValue.ShouldBeTrue();
            Directory.Exists(_settings.PendingBatchesDirectory).ShouldBeFalse();
        }
    }

    [Test]
    public async Task ATopicWithPendingResultsIsPruned()
    {
        await _store.UpdateTopicAsync("projects/garden", _ => Maybe.From(ResearchTopic.New("projects/garden", Discovered)), CancellationToken.None);
        await _store.WritePendingBatchAsync(Batch("projects/garden"), CancellationToken.None);

        await _store.UpdateTopicAsync("projects/garden", _ => Maybe<ResearchTopic>.None, CancellationToken.None);

        ItShouldRemoveTheBatch();

        void ItShouldRemoveTheBatch() => TestWiki.FileNames(_settings.PendingBatchesDirectory).ShouldBeEmpty();
    }

    [Test]
    public async Task ATopicIsSearched()
    {
        ResearchTopic searched = ResearchTopic.New("projects/garden", Discovered).Searched(Discovered.AddDays(1), ["https://example.com/one"]);
        await _store.UpdateTopicAsync("projects/garden", _ => Maybe.From(searched), CancellationToken.None);

        ResearchTopic read = (await new ResearchStore(Options.Create(_settings)).ListTopicsAsync(CancellationToken.None)).Value.Single();

        ItShouldKeepWhenAndWhatItSaw();

        void ItShouldKeepWhenAndWhatItSaw() => read.ShouldSatisfyAllConditions(
            topic => topic.LastSearchedAt.ShouldBe(Discovered.AddDays(1)),
            topic => topic.SeenUrls.ShouldBe(new Dictionary<string, DateTimeOffset> { ["https://example.com/one"] = Discovered.AddDays(1) }));
    }

    [Test]
    public async Task NoDomainsHaveBeenBlocked()
    {
        Result<BlockedDomains, Error> blocked = await _store.ReadBlockedDomainsAsync(CancellationToken.None);

        ItShouldBlockNothing();

        void ItShouldBlockNothing() => blocked.Value.Blocked.ShouldBeEmpty();
    }

    [Test]
    public async Task TheAuthorBlockedDomains()
    {
        Directory.CreateDirectory(_settings.ResearchDirectory);
        await File.WriteAllTextAsync(_settings.BlockedDomainsPath, """{ "blocked": ["contentfarm.example"] }""");

        Result<BlockedDomains, Error> blocked = await _store.ReadBlockedDomainsAsync(CancellationToken.None);

        ItShouldReadThem();

        void ItShouldReadThem() => blocked.Value.Blocked.ShouldBe(["contentfarm.example"]);
    }

    [Test]
    [Arguments("""{ "blocked": null }""")]
    [Arguments("{ not json")]
    public async Task TheBlockedDomainsAreCorrupt(string content)
    {
        Directory.CreateDirectory(_settings.ResearchDirectory);
        await File.WriteAllTextAsync(_settings.BlockedDomainsPath, content);

        Result<BlockedDomains, Error> blocked = await _store.ReadBlockedDomainsAsync(CancellationToken.None);

        ItShouldRefuseToReadThem();

        void ItShouldRefuseToReadThem() => blocked.Error.ShouldBeOfType<ResearchStoreError>();
    }

    private static ResearchBatch Batch(string topic) => new(
        topic,
        Discovered,
        [
            new BatchResult(
                "raised beds",
                "https://example.com/beds",
                new SearchResult("https://www.example.com/beds/", "Raised beds", "How to build them.", "SearXNG", Discovered.AddDays(-1)))
        ]);
}
