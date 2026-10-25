using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Notes;

namespace KnowledgeBase.Agent.Tests.Notes;

public sealed class ResearchNoteHeaderTests
{
    [Test]
    public void AResearchNoteIsRead()
    {
        const string note = """
                            ---
                            origin: research
                            topic: projects/knowledge-base
                            sources:
                              - https://example.com/article
                              - https://news.ycombinator.com/item?id=1
                            ---

                            A new release of the agent framework is out.
                            """;

        Result<NoteProvenance, Error> provenance = ResearchNoteHeader.Parse(note);

        ItShouldBeResearch();

        void ItShouldBeResearch() => provenance.Value.ShouldSatisfyAllConditions(
            value => value.Origin.ShouldBe(FactOrigin.Research),
            value => value.Topic.ShouldBe("projects/knowledge-base"),
            value => value.Sources.ShouldBe(["https://example.com/article", "https://news.ycombinator.com/item?id=1"]));
    }

    [Test]
    [Arguments("Susan gave feedback on the design review.")]
    [Arguments("---\nA note that starts with a horizontal rule.\n---\nAnd has another.")]
    [Arguments("---\ntitle: Front matter from another tool\n---\nBody.")]
    [Arguments("---\norigin: author\n---\nThe author marked this note as their own.")]
    [Arguments("")]
    public void AnAuthorNoteIsRead(string note)
    {
        Result<NoteProvenance, Error> provenance = ResearchNoteHeader.Parse(note);

        ItShouldBeTheAuthors();

        void ItShouldBeTheAuthors() => provenance.Value.ShouldBe(NoteProvenance.Author);
    }

    [Test]
    [Arguments("---\r\norigin: research\r\ntopic: learning/rust.md\r\nsources:\r\n  - https://example.com/a\r\n---\r\nBody.")]
    [Arguments("---\n  Origin: Research\nTopic: learning/rust.md\nSOURCES:\n  - https://example.com/a\n---\nBody.")]
    public void AResearchNoteIsWrittenLoosely(string note)
    {
        Result<NoteProvenance, Error> provenance = ResearchNoteHeader.Parse(note);

        ItShouldReadTheHeader();

        void ItShouldReadTheHeader() => provenance.Value.ShouldSatisfyAllConditions(
            value => value.Origin.ShouldBe(FactOrigin.Research),
            value => value.Topic.ShouldBe("learning/rust.md"),
            value => value.Sources.ShouldBe(["https://example.com/a"]));
    }

    [Test]
    [Arguments("---\norigin: research\ntopic: projects/a\nsources:\n  - https://example.com/a\nBody.", "never closed")]
    [Arguments("---\norigin: someone\n---\nBody.", "must be `research` or `author`")]
    [Arguments("---\norigin: research\nsources:\n  - https://example.com/a\n---\nBody.", "needs a `topic`")]
    [Arguments("---\norigin: research\ntopic: projects/a\n---\nBody.", "at least one source")]
    [Arguments("---\norigin: research\ntopic: projects/a\nsources:\n---\nBody.", "at least one source")]
    [Arguments("---\norigin: research\ntopic: projects/a\nsources:\n  - ftp://example.com/a\n---\nBody.", "not an absolute http(s) URL")]
    [Arguments("---\norigin: research\ntopic: projects/a\nsources:\n  - example.com/a\n---\nBody.", "not an absolute http(s) URL")]
    [Arguments("---\norigin: research\ntopic: projects/a\nsources: https://example.com/a\n---\nBody.", "on its own line")]
    [Arguments("---\norigin: research\ntopic: projects/a\ntopic: projects/b\nsources:\n  - https://example.com/a\n---\nBody.", "more than once")]
    [Arguments("---\norigin: research\ntopic: projects/a\nauthor: someone\nsources:\n  - https://example.com/a\n---\nBody.", "not a known key")]
    [Arguments("---\norigin: research\n  - https://example.com/a\ntopic: projects/a\n---\nBody.", "must be under `sources:`")]
    [Arguments("---\norigin: research\njust some words\n---\nBody.", "not a `key: value` pair")]
    [Arguments("---\norigin: research\ntopic: ../secrets\nsources:\n  - https://example.com/a\n---\nBody.", "relative to the wiki root")]
    [Arguments("---\norigin: research\ntopic: /etc/passwd\nsources:\n  - https://example.com/a\n---\nBody.", "relative to the wiki root")]
    [Arguments("---\norigin: research\ntopic: C:\\wiki\nsources:\n  - https://example.com/a\n---\nBody.", "relative to the wiki root")]
    [Arguments("---\norigin: author\ntopic: projects/a\n---\nBody.", "has no `topic` or `sources`")]
    public void AResearchHeaderIsMalformed(string note, string problem)
    {
        Result<NoteProvenance, Error> provenance = ResearchNoteHeader.Parse(note);

        ItShouldBeRefused();
        ItShouldExplainTheProblem();

        void ItShouldBeRefused() => provenance.Error.ShouldBeOfType<MalformedResearchHeaderError>();

        void ItShouldExplainTheProblem() => provenance.Error.Message.ShouldContain(problem, Case.Sensitive);
    }
}
