using KnowledgeBase.Agent.Research.Fetch;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research.Fetch;

public sealed class ReadabilityExtractorTests
{
    private static readonly Uri Article = new("https://example.com/blog/atlas-2");

    private readonly ReadabilityExtractor _extractor = new();

    [Test]
    public void APageHasATitle()
    {
        ExtractedPage page = _extractor.Extract(Article, Page("<title>Atlas 2 released</title>"));

        ItShouldKeepTheTitle();

        void ItShouldKeepTheTitle() => page.Title.ShouldBe("Atlas 2 released");
    }

    [Test]
    public void APageHasNoTitle()
    {
        ExtractedPage page = _extractor.Extract(Article, Page(head: string.Empty));

        ItShouldHaveNone();

        void ItShouldHaveNone() => page.Title.ShouldBeNull();
    }

    [Test]
    public void APageTitleSpansLinesAndHoldsMarkdown()
    {
        ExtractedPage page = _extractor.Extract(Article, Page("<title>\n  # Atlas 2\n\n  **released**  \n</title>"));

        ItShouldBeMadeOneLine();

        void ItShouldBeMadeOneLine() => page.Title.ShouldBe("# Atlas 2 **released**");
    }

    [Test]
    public void APageTitleIsLong()
    {
        ExtractedPage page = _extractor.Extract(Article, Page($"<title>{new string('a', 1000)}</title>"));

        ItShouldBeCutToTheLimit();

        void ItShouldBeCutToTheLimit() => page.Title.ShouldBe($"{new string('a', FetchedPage.MaxTitleLength - 1)}…");
    }

    private static string Page(string head) =>
        $$"""
        <!doctype html>
        <html>
        <head>{{head}}</head>
        <body>
          <main>
            <article>
              {{string.Join("\n", Enumerable.Range(1, 6).Select(paragraph =>
                  $"<p>Paragraph {paragraph} of the release notes describes, at some length, what changed in the renderer, the router and the build tooling.</p>"))}}
            </article>
          </main>
        </body>
        </html>
        """;
}
