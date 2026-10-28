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
            Result("https://example.com/a", "Atlas 2", ScoutVerdict.Keep("Atlas 2 ships.", ["Atlas 2 is out."], Searched)),
            Result("https://example.com/b", "A talk", ScoutVerdict.Keep("A talk on Atlas.", [], Searched))
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

    private static BatchResult Result(string url, string title, ScoutVerdict verdict) =>
        new("a query", url, new SearchResult(url, title, null, "SearXNG", null), FetchedPage.Content(Searched, "Page text that must not appear.", false), verdict);
}
