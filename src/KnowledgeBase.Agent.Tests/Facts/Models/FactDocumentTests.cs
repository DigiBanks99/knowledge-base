using System.Text.Json;

using KnowledgeBase.Agent.Facts.Models;

namespace KnowledgeBase.Agent.Tests.Facts.Models;

public sealed class FactDocumentTests
{
    [Test]
    public void ADocumentFromBeforeProvenanceIsRead()
    {
        const string json = """
                            {
                              "sourceNote": "note-2026-10-24-101500.md",
                              "facts": [
                                {
                                  "subject": { "name": "Susan", "description": "A colleague." },
                                  "observations": [
                                    { "summary": "Gave feedback", "detail": "On the design review.", "priority": 2, "relatedSubjects": [] }
                                  ]
                                }
                              ]
                            }
                            """;

        FactDocument document = JsonSerializer.Deserialize<FactDocument>(json, JsonSerializerOptions.Web)!;

        ItShouldBeTheAuthors();
        ItShouldLeaveTheSubjectUnclassified();
        ItShouldHaveNoSources();

        void ItShouldBeTheAuthors()
        {
            document.Origin.ShouldBe(FactOrigin.Author);
            document.Topic.ShouldBeNull();
        }

        void ItShouldLeaveTheSubjectUnclassified() =>
            document.Facts.ShouldHaveSingleItem().Subject.Kind.ShouldBeNull();

        void ItShouldHaveNoSources() =>
            document.Facts.Single().Observations.ShouldHaveSingleItem().Sources.ShouldBeNull();
    }

    [Test]
    public void AResearchDocumentIsWritten()
    {
        FactDocument document = new(
            "note-2026-10-24-101500.md",
            [
                new Fact(
                    new FactSubject("Microsoft", "Publisher of the agent framework.", SubjectKind.Organisation),
                    [new Observation("Released version 2", "Adds workflows.", 2, [], ["https://example.com/a"])])
            ],
            FactOrigin.Research,
            "projects/knowledge-base");

        string json = JsonSerializer.Serialize(document, JsonSerializerOptions.Web);
        FactDocument read = JsonSerializer.Deserialize<FactDocument>(json, JsonSerializerOptions.Web)!;

        ItShouldNameTheOriginAndKindInWords();
        ItShouldReadBackTheProvenance();

        void ItShouldNameTheOriginAndKindInWords()
        {
            json.ShouldContain("\"origin\":\"Research\"", Case.Sensitive);
            json.ShouldContain("\"kind\":\"Organisation\"", Case.Sensitive);
        }

        void ItShouldReadBackTheProvenance() => read.ShouldSatisfyAllConditions(
            value => value.Origin.ShouldBe(FactOrigin.Research),
            value => value.Topic.ShouldBe("projects/knowledge-base"),
            value => value.Facts.Single().Subject.Kind.ShouldBe(SubjectKind.Organisation),
            value => value.Facts.Single().Observations.Single().Sources.ShouldBe(["https://example.com/a"]));
    }
}
