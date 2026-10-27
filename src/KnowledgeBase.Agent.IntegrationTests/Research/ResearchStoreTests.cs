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
}
