using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research.Models;

/// <summary>
/// Maps wiki paths to research topics. Every top-level entry of a researched section is a topic: a folder with all its
/// pages (<c>projects/foo</c>), or a single page (<c>learning/bar.md</c>). A topic is identified by that path, so
/// reshuffling pages inside a project never changes which topic they belong to.
/// </summary>
public static class TopicPaths
{
    public static IReadOnlyList<string> Sections { get; } =
        [.. WikiSections.All.Where(section => section.Researched).Select(section => section.Folder)];

    public const string PageExtension = ".md";

    private const string SlugSeparator = "--";
    private const string IndexPage = "index.md";

    /// <summary>
    /// The topic a wiki-relative path belongs to, or none for section index pages, other sections and paths leaving the
    /// wiki. A top-level entry ending in <c>.md</c> is a page topic; any other is a folder topic, as folder names may
    /// contain dots (<c>projects/v1.2-migration</c>). Whether it really is a topic is for <see cref="IWikiTopics"/> to
    /// say from the disk: a stray file such as <c>projects/diagram.png</c> maps to a folder topic that never exists.
    /// </summary>
    public static Maybe<string> TopicOf(string wikiPath)
    {
        string[] segments = wikiPath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment != ".")
            .ToArray();
        if (segments.Length < 2
            || segments.Any(segment => segment == ".." || WikiPaths.IsRepositoryName(segment))
            || !Sections.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
        {
            return Maybe.None;
        }

        string section = segments[0].ToLowerInvariant();
        string entry = segments[1];
        bool isSectionIndex = segments.Length == 2 && entry.Equals(IndexPage, StringComparison.OrdinalIgnoreCase);
        return isSectionIndex ? Maybe.None : $"{section}/{entry}";
    }

    /// <summary>The file name a topic's state is kept under: its path with <c>/</c> replaced by <c>--</c>.</summary>
    public static string Slug(string topic) => topic.Replace("/", SlugSeparator, StringComparison.Ordinal);

    /// <summary>
    /// The topic a slug (from a file name or the UI) names. Topics are always <c>section/entry</c> and sections never
    /// contain <c>--</c>, so splitting on the first one is reversible even when the entry contains <c>--</c>. A slug
    /// that does not name a topic exactly, such as one reaching outside a researched section, is none.
    /// </summary>
    public static Maybe<string> FromSlug(string slug)
    {
        int separator = slug.IndexOf(SlugSeparator, StringComparison.Ordinal);
        if (separator <= 0)
        {
            return Maybe.None;
        }

        string path = $"{slug[..separator]}/{slug[(separator + SlugSeparator.Length)..]}";
        return TopicOf(path).Where(topic => topic == path);
    }
}
