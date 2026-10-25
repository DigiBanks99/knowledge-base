using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Research;

/// <summary>The one-off backfill of the deny-list from the people the wiki already has, run as the app starts.</summary>
public sealed class SeedDenyListFromPeopleTests : IAsyncDisposable
{
    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    private string PeopleDirectory => Path.Combine(_wiki.Settings.WikiDirectory, "people");

    [Test]
    public async Task TheWikiAlreadyHasPeople()
    {
        _wiki.Write(Path.Combine(PeopleDirectory, "jane-doe"), "index.md", "# Jane Doe\n");
        _wiki.Write(Path.Combine(PeopleDirectory, "john-smith"), "index.md", "# John Smith\n");

        _wiki.Start();
        DenyList denyList = await ReadDenyListAsync();

        ItShouldDenyEveryPerson();
        ItShouldIgnoreTheSectionsIndexPage();
        ItShouldRecordThatItSeeded();

        void ItShouldDenyEveryPerson() => denyList.Entries.ShouldBe(
        [
            new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied),
            new DenyListEntry("John Smith", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied)
        ], ignoreOrder: true);

        void ItShouldIgnoreTheSectionsIndexPage() => denyList.Find("index").ShouldBeNull();

        void ItShouldRecordThatItSeeded() => denyList.SeededFromPeople.ShouldBeTrue();
    }

    [Test]
    public async Task TheDenyListWasAlreadySeeded()
    {
        DenyList seeded = new([new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Manual, DenyListState.Allowed)], SeededFromPeople: true);
        await _wiki.Get<IResearchStore>().UpdateDenyListAsync(_ => seeded, CancellationToken.None);
        _wiki.Write(Path.Combine(PeopleDirectory, "jane-doe"), "index.md", "# Jane Doe\n");
        _wiki.Write(Path.Combine(PeopleDirectory, "john-smith"), "index.md", "# John Smith\n");

        _wiki.Start();
        DenyList denyList = await ReadDenyListAsync();

        ItShouldNotSeedAgain();

        void ItShouldNotSeedAgain() => denyList.Entries.ShouldBe(seeded.Entries);
    }

    [Test]
    public async Task TheAuthorHadAllowedSomeoneBeforeTheSeed()
    {
        DenyList allowed = new([new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Manual, DenyListState.Allowed)]);
        await _wiki.Get<IResearchStore>().UpdateDenyListAsync(_ => allowed, CancellationToken.None);
        _wiki.Write(Path.Combine(PeopleDirectory, "jane-doe"), "index.md", "# Jane Doe\n");

        _wiki.Start();
        DenyList denyList = await ReadDenyListAsync();

        ItShouldKeepTheAuthorsDecision();

        void ItShouldKeepTheAuthorsDecision() => denyList.Entries.ShouldHaveSingleItem()
            .ShouldBe(new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Manual, DenyListState.Allowed));
    }

    private async Task<DenyList> ReadDenyListAsync() =>
        (await _wiki.Get<IResearchStore>().ReadDenyListAsync(CancellationToken.None)).Value;
}
