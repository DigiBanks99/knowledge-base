using System.Text.RegularExpressions;

using AngleSharp.Html.Parser;

using ReverseMarkdown;

using SmartReader;

namespace KnowledgeBase.Agent.Research.Fetch;

/// <summary>Reduces a fetched HTML page to its main content as markdown.</summary>
internal interface IContentExtractor
{
    /// <summary>The page's main content as markdown; empty when it has none.</summary>
    string ToMarkdown(Uri uri, string html);
}

/// <summary>
/// Mozilla's Readability algorithm (through SmartReader) picks the article out of the page chrome, and ReverseMarkdown
/// turns it into markdown. SmartReader is only ever given HTML already fetched, so it never makes a request itself.
/// </summary>
internal sealed partial class ReadabilityExtractor : IContentExtractor
{
    // Kept only when Readability finds no article, as the next best guess at the content.
    private static readonly string[] Chrome = ["script", "style", "noscript", "nav", "header", "footer", "aside", "form", "iframe", "svg"];

    // Media cost tokens and tell triage nothing, and an image left in would be loaded by the author's browser when
    // the Topics page shows the content.
    private static readonly string[] Media = ["img", "picture", "source", "video", "audio", "svg", "iframe", "object", "embed"];

    public string ToMarkdown(Uri uri, string html)
    {
        Article article = new Reader(uri.ToString(), html).GetArticle();
        string content = !string.IsNullOrWhiteSpace(article.Content)
            ? Without(article.Content, Media)
            : Without(html, [.. Chrome, .. Media]);

        Converter converter = new(new Config
        {
            GithubFlavored = true,
            Formatting = { RemoveComments = true },
            Links = { SmartHref = true },
            Tags = { Unknown = Config.UnknownTagsOption.Bypass }
        });

        return BlankLines().Replace(converter.Convert(content), "\n\n").Trim();
    }

    /// <summary>The body of <paramref name="html"/> with every element matching <paramref name="tags"/> removed.</summary>
    private static string Without(string html, string[] tags)
    {
        using AngleSharp.Html.Dom.IHtmlDocument document = new HtmlParser().ParseDocument(html);
        foreach (AngleSharp.Dom.IElement element in document.QuerySelectorAll(string.Join(',', tags)).ToList())
        {
            element.Remove();
        }

        return document.Body?.InnerHtml ?? string.Empty;
    }

    [GeneratedRegex(@"\n[ \t]*(\n[ \t]*){2,}")]
    private static partial Regex BlankLines();
}
