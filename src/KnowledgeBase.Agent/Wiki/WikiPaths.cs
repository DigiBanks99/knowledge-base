using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki;

/// <summary>
/// Converts between wiki-relative paths (as agents and the UI use them) and full paths, and refuses any path that
/// would leave the wiki directory.
/// </summary>
internal sealed class WikiPaths(IOptions<WikiConfigurationSettings> settings)
{
    public string Root { get; } = Path.GetFullPath(settings.Value.WikiDirectory);

    public bool TryResolve(string path, out string fullPath)
    {
        fullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? "." : path, Root);
        return fullPath.Equals(Root, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public string Resolve(string path) => TryResolve(path, out string fullPath)
        ? fullPath
        : throw new InvalidOperationException($"The path '{path}' is outside the wiki. All paths must be relative to the wiki root.");

    public string ToRelative(string fullPath)
    {
        string relativePath = Path.GetRelativePath(Root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        return Directory.Exists(fullPath) ? $"{relativePath}/" : relativePath;
    }
}
