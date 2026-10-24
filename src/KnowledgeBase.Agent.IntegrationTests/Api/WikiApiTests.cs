using System.Net;

using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

public sealed class WikiApiTests : IDisposable
{
    private readonly TestApi _api = new();

    public void Dispose() => _api.Dispose();

    [Test]
    public async Task APageIsRequested()
    {
        HttpResponseMessage response = await _api.Client().GetAsync("/api/wiki/page?path=people/index.md");

        ItShouldFindIt();
        await ItShouldReturnItsContent();

        void ItShouldFindIt() => response.StatusCode.ShouldBe(HttpStatusCode.OK);

        async Task ItShouldReturnItsContent()
        {
            WikiPage page = await TestApi.ReadAsync<WikiPage>(response);
            page.Path.ShouldBe("people/index.md");
            page.Content.ShouldBe(WikiSections.SectionIndex(WikiSections.All.Single(section => section.Folder == "people")));
        }
    }

    [Test]
    [Arguments("../facts/collection.json")]
    [Arguments("../notes/new/note-2026-10-24-101500.md")]
    [Arguments("people/../../runs/run.md")]
    [Arguments("people/index.txt")]
    public async Task SomethingOtherThanAWikiPageIsRequested(string path)
    {
        HttpResponseMessage response = await _api.Client().GetAsync($"/api/wiki/page?path={Uri.EscapeDataString(path)}");

        ItShouldRefuse();

        void ItShouldRefuse() => response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AMissingPageIsRequested()
    {
        HttpResponseMessage response = await _api.Client().GetAsync("/api/wiki/page?path=people/nobody.md");

        ItShouldNotFindIt();

        void ItShouldNotFindIt() => response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TheTreeIsRequested()
    {
        HttpResponseMessage response = await _api.Client().GetAsync("/api/wiki/tree");
        WikiNode root = await TestApi.ReadAsync<WikiNode>(response);

        ItShouldStartAtTheRoot();
        ItShouldListFoldersByNameBeforePages();
        ItShouldGiveEveryNodeItsWikiPath();

        void ItShouldStartAtTheRoot() => root.ShouldSatisfyAllConditions(
            node => node.Path.ShouldBe(string.Empty),
            node => node.Type.ShouldBe(WikiNodeType.Directory));

        void ItShouldListFoldersByNameBeforePages() => root.Children.Select(child => child.Name).ShouldBe(
            [.. WikiSections.All.Select(section => section.Folder).Order(StringComparer.OrdinalIgnoreCase), "index.md"]);

        void ItShouldGiveEveryNodeItsWikiPath() => root.Children.Single(child => child.Name == "people")
            .Children.ShouldHaveSingleItem().Path.ShouldBe("people/index.md");
    }
}