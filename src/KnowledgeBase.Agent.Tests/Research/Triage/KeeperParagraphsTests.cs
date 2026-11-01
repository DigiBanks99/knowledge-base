using KnowledgeBase.Agent.Research.Triage;

namespace KnowledgeBase.Agent.Tests.Research.Triage;

public sealed class KeeperParagraphsTests
{
    private const string Prose = "Atlas 2 ships a new scheduler and drops the old plugin API.";
    private const string MoreProse = "Upgrading takes an afternoon for most projects, the maintainers say.";

    [Test]
    public void APageHasHeadingsAndFragments()
    {
        IReadOnlyList<string> paragraphs = KeeperParagraphs.From($"# Atlas 2\n\n{Prose}\n\nRead more\n\n## Upgrading\n\n{MoreProse}");

        ItShouldKeepOnlyTheProse();

        void ItShouldKeepOnlyTheProse() => paragraphs.ShouldBe([Prose, MoreProse]);
    }

    [Test]
    public void AHeadingSitsStraightAboveItsText()
    {
        IReadOnlyList<string> paragraphs = KeeperParagraphs.From($"## Release notes\n{Prose}");

        ItShouldKeepTheText();

        void ItShouldKeepTheText() => paragraphs.ShouldBe([Prose]);
    }

    [Test]
    public void AParagraphIsWrappedOverLines()
    {
        IReadOnlyList<string> paragraphs = KeeperParagraphs.From("Atlas 2 ships a new scheduler\nand drops   the old plugin API.");

        ItShouldBeOneLine();

        void ItShouldBeOneLine() => paragraphs.ShouldBe([Prose]);
    }

    [Test]
    [Arguments("[![build](https://img.shields.io/badge/build-passing-green?logo=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8)](https://ci.example.com/atlas)")]
    [Arguments("![The Atlas logo, a globe held up by a figure in a long robe](https://example.com/logo.png)")]
    [Arguments("[![build](https://img.shields.io/b.svg)](https://ci.example.com) [![npm](https://img.shields.io/n.svg)](https://npm.example.com/atlas)")]
    [Arguments("- [Documentation for every Atlas release](https://example.com/docs) | [Changelog](https://example.com/changes)")]
    [Arguments("https://example.com/a-very-long-link/that-goes-on-for-long-enough-to-count")]
    public void APageHasALineOfImagesBadgesOrLinks(string line)
    {
        IReadOnlyList<string> paragraphs = KeeperParagraphs.From($"{line}\n\n{Prose}");

        ItShouldLeaveTheLineOut();

        void ItShouldLeaveTheLineOut() => paragraphs.ShouldBe([Prose]);
    }

    [Test]
    public void AParagraphLinksInItsText()
    {
        string linked = "Atlas 2 ships a [new scheduler](https://example.com/scheduler) and drops the old plugin API.";

        IReadOnlyList<string> paragraphs = KeeperParagraphs.From(linked);

        ItShouldKeepTheParagraph();

        void ItShouldKeepTheParagraph() => paragraphs.ShouldBe([linked]);
    }

    [Test]
    public void ABadgeSitsAboveAParagraphWithoutABlankLine()
    {
        IReadOnlyList<string> paragraphs = KeeperParagraphs.From($"[![build](https://img.shields.io/b.svg)](https://ci.example.com)\n{Prose}");

        ItShouldKeepOnlyTheProse();

        void ItShouldKeepOnlyTheProse() => paragraphs.ShouldBe([Prose]);
    }
}
