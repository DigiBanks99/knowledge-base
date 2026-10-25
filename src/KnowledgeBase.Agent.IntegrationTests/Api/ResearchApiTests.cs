using System.Net;
using System.Net.Http.Json;

using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

public sealed class ResearchApiTests : IDisposable
{
    private static readonly DenyListEntry JaneDoe = new("Jane Doe", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied);
    private static readonly DenyListEntry Contoso = new("Contoso", SubjectKind.Organisation, DenyListSource.Auto, DenyListState.Denied);

    private readonly TestApi _api = new();

    public void Dispose() => _api.Dispose();

    [Test]
    public async Task TheDenyListIsRequested()
    {
        await DenyAsync(JaneDoe, Contoso);

        HttpResponseMessage response = await _api.Client().GetAsync("/api/research/deny-list");
        IReadOnlyList<DenyListEntry> entries = await TestApi.ReadAsync<IReadOnlyList<DenyListEntry>>(response);

        ItShouldSucceed();
        ItShouldListEveryEntryByName();

        void ItShouldSucceed() => response.StatusCode.ShouldBe(HttpStatusCode.OK);

        void ItShouldListEveryEntryByName() => entries.ShouldBe([Contoso, JaneDoe]);
    }

    [Test]
    public async Task TheAuthorAllowsAName()
    {
        await DenyAsync(JaneDoe);

        HttpResponseMessage response = await _api.Client().PutAsJsonAsync(
            "/api/research/deny-list/state",
            new SetDenyListEntryStateCommand("jane doe", DenyListState.Allowed),
            TestApi.Json);
        DenyListEntry entry = await TestApi.ReadAsync<DenyListEntry>(response);
        DenyList stored = (await _api.Get<IResearchStore>().ReadDenyListAsync(CancellationToken.None)).Value;

        ItShouldReturnTheAuthorsDecision();
        ItShouldStoreIt();

        void ItShouldReturnTheAuthorsDecision() =>
            entry.ShouldBe(JaneDoe with { Source = DenyListSource.Manual, State = DenyListState.Allowed });

        void ItShouldStoreIt() => stored.Entries.ShouldBe([entry]);
    }

    [Test]
    public async Task TheAuthorTogglesAnUnknownName()
    {
        HttpResponseMessage response = await _api.Client().PutAsJsonAsync(
            "/api/research/deny-list/state",
            new SetDenyListEntryStateCommand("John Smith", DenyListState.Allowed),
            TestApi.Json);

        ItShouldBeNotFound();

        void ItShouldBeNotFound() => response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ANameIsToggledWithoutTheCsrfHeader()
    {
        await DenyAsync(JaneDoe);

        HttpResponseMessage response = await _api.Client(sendsCsrfHeader: false).PutAsJsonAsync(
            "/api/research/deny-list/state",
            new SetDenyListEntryStateCommand(JaneDoe.Name, DenyListState.Allowed),
            TestApi.Json);
        DenyList stored = (await _api.Get<IResearchStore>().ReadDenyListAsync(CancellationToken.None)).Value;

        ItShouldRefuseTheChange();
        ItShouldKeepTheNameDenied();

        void ItShouldRefuseTheChange() => response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        void ItShouldKeepTheNameDenied() => stored.Entries.ShouldBe([JaneDoe]);
    }

    [Test]
    public async Task SomeoneSignedOutRequestsTheDenyList()
    {
        HttpResponseMessage response = await _api.Client(objectId: null).GetAsync("/api/research/deny-list");

        ItShouldBeRefused();

        void ItShouldBeRefused() => response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task DenyAsync(params DenyListEntry[] entries)
    {
        // Reaching the services starts the app, which seeds the deny-list first; the entries are added after it.
        await _api.Get<IResearchStore>().UpdateDenyListAsync(
            denyList => entries.Aggregate(denyList, (list, entry) => list.WithAutomatic(entry.Name, entry.Kind)),
            CancellationToken.None);
    }
}
