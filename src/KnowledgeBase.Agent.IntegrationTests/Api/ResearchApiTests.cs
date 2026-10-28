using System.Net;
using System.Net.Http.Json;

using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Search;

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

    [Test]
    public async Task TheTopicsAreRequested()
    {
        await DiscoverAsync("projects/garden", "learning/rust.md");

        HttpResponseMessage response = await _api.Client().GetAsync("/api/research/topics");
        IReadOnlyList<TopicSummary> topics = await TestApi.ReadAsync<IReadOnlyList<TopicSummary>>(response);

        ItShouldSucceed();
        ItShouldListEveryTopicByPath();

        void ItShouldSucceed() => response.StatusCode.ShouldBe(HttpStatusCode.OK);

        void ItShouldListEveryTopicByPath() => topics.Select(topic => (topic.Path, topic.Slug, topic.State)).ShouldBe(
        [
            ("learning/rust.md", "learning--rust.md", TopicState.Idle),
            ("projects/garden", "projects--garden", TopicState.Idle)
        ]);
    }

    [Test]
    public async Task TheAuthorMutesATopic()
    {
        await DiscoverAsync("projects/garden");

        HttpResponseMessage response = await _api.Client().PutAsJsonAsync(
            "/api/research/topics/projects--garden/muted",
            new { muted = true },
            TestApi.Json);
        IReadOnlyList<ResearchTopic> stored = (await _api.Get<IResearchStore>().ListTopicsAsync(CancellationToken.None)).Value;

        ItShouldSucceed();
        ItShouldStoreTheDecision();

        void ItShouldSucceed() => response.StatusCode.ShouldBe(HttpStatusCode.OK);

        void ItShouldStoreTheDecision() => stored.ShouldHaveSingleItem().Muted.ShouldBeTrue();
    }

    [Test]
    [Arguments("projects--unknown", HttpStatusCode.NotFound)]
    [Arguments("people--jane-doe", HttpStatusCode.BadRequest)]
    [Arguments("..--notes", HttpStatusCode.BadRequest)]
    public async Task TheAuthorMutesSomethingThatIsNotAKnownTopic(string slug, HttpStatusCode status)
    {
        HttpResponseMessage response = await _api.Client().PutAsJsonAsync(
            $"/api/research/topics/{slug}/muted",
            new { muted = true },
            TestApi.Json);

        ItShouldBeRefused();

        void ItShouldBeRefused() => response.StatusCode.ShouldBe(status);
    }

    [Test]
    public async Task TheAuthorResearchesATopicNow()
    {
        await CurateAsync("projects/garden", "raised beds");
        _api.Web.Respond = _ => FakeWeb.Json("""
            { "hits": [ { "objectID": "1", "title": "Building raised beds", "url": "https://example.com/beds", "created_at_i": 4102444800 } ] }
            """);

        HttpResponseMessage response = await _api.Client().PostAsync("/api/research/topics/projects--garden/research", null);
        TopicSearchOutcome outcome = await TestApi.ReadAsync<TopicSearchOutcome>(response);
        HttpResponseMessage results = await _api.Client().GetAsync("/api/research/topics/projects--garden/results");
        ResearchBatch batch = await TestApi.ReadAsync<ResearchBatch>(results);
        IReadOnlyList<TopicSummary> topics = await TestApi.ReadAsync<IReadOnlyList<TopicSummary>>(
            await _api.Client().GetAsync("/api/research/topics"));

        ItShouldSearchHackerNewsForItsQuery();
        ItShouldReportWhatItKept();
        ItShouldShowThePendingResults();
        ItShouldShowTheTopicAsTriaging();

        void ItShouldSearchHackerNewsForItsQuery() =>
            _api.Web.Requests.ShouldContain(uri => uri.AbsoluteUri.Contains("search_by_date?query=raised%20beds", StringComparison.Ordinal));

        void ItShouldReportWhatItKept() => outcome.ShouldBe(new TopicSearchOutcome("projects/garden", 1, 1));

        void ItShouldShowThePendingResults() =>
            batch.Results.ShouldHaveSingleItem().Result.Title.ShouldBe("Building raised beds");

        void ItShouldShowTheTopicAsTriaging() => topics.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            topic => topic.State.ShouldBe(TopicState.Triaging),
            topic => topic.PendingResults.ShouldBe(1));
    }

    [Test]
    public async Task TheAuthorResearchesATopicThePlebHasNotCurated()
    {
        await DiscoverAsync("projects/garden");

        HttpResponseMessage response = await _api.Client().PostAsync("/api/research/topics/projects--garden/research", null);

        ItShouldBeAConflict();

        void ItShouldBeAConflict() => response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task TheAuthorResearchesATopicWithoutTheCsrfHeader()
    {
        await CurateAsync("projects/garden", "raised beds");

        HttpResponseMessage response = await _api.Client(sendsCsrfHeader: false).PostAsync("/api/research/topics/projects--garden/research", null);

        ItShouldBeRefused();
        ItShouldSearchNothing();

        void ItShouldBeRefused() => response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        void ItShouldSearchNothing() => _api.Web.Requests.ShouldNotContain(uri => uri.AbsolutePath.EndsWith("search_by_date", StringComparison.Ordinal));
    }

    [Test]
    public async Task TheResultsOfATopicWithNoneWaitingAreRequested()
    {
        await DiscoverAsync("projects/garden");

        HttpResponseMessage response = await _api.Client().GetAsync("/api/research/topics/projects--garden/results");

        ItShouldBeNotFound();

        void ItShouldBeNotFound() => response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task CurateAsync(string topic, string query) =>
        await _api.Get<IResearchStore>().UpdateTopicAsync(
            topic,
            _ => Maybe.From(ResearchTopic.New(topic, DateTimeOffset.UnixEpoch) with { Brief = "A topic.", Queries = [query] }),
            CancellationToken.None);

    private async Task DiscoverAsync(params string[] topics)
    {
        IResearchStore store = _api.Get<IResearchStore>();
        foreach (string topic in topics)
        {
            await store.UpdateTopicAsync(
                topic,
                _ => Maybe.From(ResearchTopic.New(topic, DateTimeOffset.UnixEpoch)),
                CancellationToken.None);
        }
    }

    private async Task DenyAsync(params DenyListEntry[] entries)
    {
        // Reaching the services starts the app, which seeds the deny-list first; the entries are added after it.
        await _api.Get<IResearchStore>().UpdateDenyListAsync(
            denyList => entries.Aggregate(denyList, (list, entry) => list.WithAutomatic(entry.Name, entry.Kind)),
            CancellationToken.None);
    }
}
