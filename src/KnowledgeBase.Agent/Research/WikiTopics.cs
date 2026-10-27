using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research;

/// <summary>The research topics as they exist in the wiki directory right now.</summary>
internal interface IWikiTopics
{
    /// <summary>
    /// Whether the topic's page, or a folder holding at least one file, is in the wiki. The file tools never remove
    /// folders, so a folder topic whose pages were all deleted or moved away is gone even though its folder is left.
    /// </summary>
    bool Exists(string topic);

    /// <summary>Every topic in the wiki: the top-level folders and pages of the researched sections.</summary>
    IReadOnlyList<string> All();

    /// <summary>
    /// The content of a wiki page, or none when it is not in the wiki or cannot be read (such as a page deleted since
    /// it was changed).
    /// </summary>
    /// <param name="wikiPath">The page's wiki path, such as <c>projects/foo/index.md</c>.</param>
    Maybe<string> ReadPage(string wikiPath);
}

internal sealed class WikiTopics(WikiPaths wikiPaths) : IWikiTopics
{
    public bool Exists(string topic)
    {
        if (!wikiPaths.TryResolve(topic, out string fullPath))
        {
            return false;
        }

        return topic.EndsWith(TopicPaths.PageExtension, StringComparison.OrdinalIgnoreCase)
            ? File.Exists(fullPath)
            : Directory.Exists(fullPath) && Directory
                .EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
                .Any(filePath => !wikiPaths.IsRepositoryPath(filePath));
    }

    public IReadOnlyList<string> All() =>
    [
        .. TopicPaths.Sections
            .Select(wikiPaths.Resolve)
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateFileSystemEntries)
            .Select(fullPath => Path.GetRelativePath(wikiPaths.Root, fullPath))
            .Select(TopicPaths.TopicOf)
            .Choose()
            .Where(Exists)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
    ];

    public Maybe<string> ReadPage(string wikiPath)
    {
        if (!wikiPaths.TryResolve(wikiPath, out string fullPath) || !File.Exists(fullPath))
        {
            return Maybe.None;
        }

        try
        {
            return File.ReadAllText(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Maybe.None;
        }
    }
}
