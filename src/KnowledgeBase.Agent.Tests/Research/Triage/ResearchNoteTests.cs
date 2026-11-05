using KnowledgeBase.Agent.Notes;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Triage;

namespace KnowledgeBase.Agent.Tests.Research.Triage;

public sealed class ResearchNoteTests
{
    private static readonly DateTimeOffset Searched = new(2026, 10, 27, 7, 0, 5, TimeSpan.Zero);

    [Test]
    public void TwoResultsWereKept()
    {
        ResearchBatch batch = new("projects/atlas", Searched, []);
        BatchResult[] kept =
        [
            Result("https://example.com/a", "Atlas 2", Verdict.Keep("Atlas 2 ships.", ["Atlas 2 is out."], Searched)),
            Result("https://example.com/b", "A talk", Verdict.Keep("A talk on Atlas.", [], Searched))
        ];

        ResearchNote note = ResearchNote.Build(batch, kept);
        string content = note.Content.ReplaceLineEndings("\n");
        Result<NoteProvenance, Error> provenance = ResearchNoteHeader.Parse(note.Content);

        ItShouldNameTheFileFromTheTopicAndTheSearch();
        ItShouldStartWithAHeaderTheNotePipelineReads();
        ItShouldGiveEachKeptResultItsSourceSummaryAndQuotes();

        void ItShouldNameTheFileFromTheTopicAndTheSearch() => note.FileName.ShouldBe("research-projects--atlas-20261027-070005.md");

        void ItShouldStartWithAHeaderTheNotePipelineReads() => provenance.Value.ShouldSatisfyAllConditions(
            value => value.Origin.ShouldBe(KnowledgeBase.Agent.Facts.Models.FactOrigin.Research),
            value => value.Topic.ShouldBe("projects/atlas"),
            value => value.Sources.ShouldBe(["https://example.com/a", "https://example.com/b"]));

        void ItShouldGiveEachKeptResultItsSourceSummaryAndQuotes() => content.ShouldSatisfyAllConditions(
            () => content.ShouldContain("## Atlas 2\n\nSource: https://example.com/a\n\nAtlas 2 ships.\n\n> Atlas 2 is out."),
            () => content.ShouldContain("## A talk\n\nSource: https://example.com/b\n\nA talk on Atlas."));
    }

    [Test]
    public void TheKeeperKeptAResultWithoutASummary()
    {
        ResearchBatch batch = new("projects/atlas", Searched, []);
        BatchResult kept = Result("https://example.com/a", "Atlas 2", Verdict.Keep(null, ["Atlas 2 is out.", "It drops plugins."], Searched) with { Kind = "release" });

        string content = ResearchNote.Build(batch, [kept]).Content.ReplaceLineEndings("\n");

        ItShouldGiveTheTitleSourceKindAndQuotes();

        void ItShouldGiveTheTitleSourceKindAndQuotes() => content.ShouldEndWith(
            "## Atlas 2\n\nSource: https://example.com/a\n\nKind: release\n\n> Atlas 2 is out.\n\n> It drops plugins.\n");
    }

    [Test]
    public void AKeptResultHasASearchSnippetAndTitle()
    {
        ResearchBatch batch = new("projects/atlas", Searched, []);
        BatchResult kept = Result("https://example.com/a", "Atlas 2", Verdict.Keep("Atlas 2 ships.", [], Searched)) with
        {
            Result = new SearchResult("https://example.com/a", "The engine's title", "The engine's snippet.", "SearXNG", null)
        };

        string content = ResearchNote.Build(batch, [kept]).Content.ReplaceLineEndings("\n");

        ItShouldBeHeadedByThePagesTitle();
        ItShouldKeepNothingTheSearchEngineWrote();

        void ItShouldBeHeadedByThePagesTitle() => content.ShouldContain("## Atlas 2\n\nSource: https://example.com/a\n\nAtlas 2 ships.");

        void ItShouldKeepNothingTheSearchEngineWrote() => content.ShouldSatisfyAllConditions(
            () => content.ShouldNotContain("Search snippet"),
            () => content.ShouldNotContain("The engine's snippet."),
            () => content.ShouldNotContain("The engine's title"));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public void AKeptResultsPageHasNoTitle(string? pageTitle)
    {
        ResearchBatch batch = new("projects/atlas", Searched, []);
        BatchResult kept = Result("https://example.com/blog/atlas-2/?ref=feed", pageTitle, Verdict.Keep("Atlas 2 ships.", [], Searched));

        string content = ResearchNote.Build(batch, [kept]).Content.ReplaceLineEndings("\n");

        ItShouldBeHeadedByTheHostAndPath();

        void ItShouldBeHeadedByTheHostAndPath() => content.ShouldContain("## example.com/blog/atlas-2\n\nSource: https://example.com/blog/atlas-2/?ref=feed");
    }

    private static BatchResult Result(string url, string? pageTitle, Verdict verdict) =>
        new(
            "a query",
            url,
            new SearchResult(url, "A search engine's title", null, "SearXNG", null),
            FetchedPage.Content(Searched, "Page text that must not appear.", false, pageTitle),
            verdict);
}
