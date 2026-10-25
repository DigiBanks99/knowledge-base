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