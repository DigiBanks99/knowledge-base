using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Tests.Wiki;

public sealed class WikiSectionsTests
{
    [Test]
    public void TheSectionsAreListed()
    {
        IReadOnlyList<string> folders = WikiSections.All.Select(section => section.Folder).ToList();

        ItShouldGiveEverySectionItsOwnFolder();
        ItShouldUseLowercaseFolderNames();

        void ItShouldGiveEverySectionItsOwnFolder() => folders.ShouldBeUnique();

        void ItShouldUseLowercaseFolderNames() =>
            folders.ShouldAllBe(folder => folder == folder.ToLowerInvariant());
    }

    [Test]
    public void TheRootIndexIsRendered()
    {
        string index = WikiSections.RootIndex();

        ItShouldHaveAHeading();
        ItShouldLinkToEverySectionIndex();
        ItShouldUseUnixLineEndings();

        void ItShouldHaveAHeading() => index.ShouldStartWith("# Wiki\n");

        void ItShouldLinkToEverySectionIndex()
        {
            foreach (WikiSection section in WikiSections.All)
            {
                index.ShouldContain($"- [{section.Title}]({section.Folder}/index.md): {section.Purpose}");
            }
        }

        void ItShouldUseUnixLineEndings() => index.ShouldNotContain('\r');
    }

    [Test]
    public void ASectionIndexIsRendered()
    {
        WikiSection section = new("people", "People", "Anyone with a relationship to the author.");

        string index = WikiSections.SectionIndex(section);

        ItShouldBeHeadedWithTheTitle();
        ItShouldDescribeTheSection();
        ItShouldLinkBackToTheRoot();
        ItShouldUseUnixLineEndings();

        void ItShouldBeHeadedWithTheTitle() => index.ShouldStartWith("# People\n");

        void ItShouldDescribeTheSection() => index.ShouldContain(section.Purpose);

        void ItShouldLinkBackToTheRoot() => index.ShouldContain("[Back to the wiki root](../index.md)");

        void ItShouldUseUnixLineEndings() => index.ShouldNotContain('\r');
    }
}
