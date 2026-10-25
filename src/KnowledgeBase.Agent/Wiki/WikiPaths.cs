using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki;

/// <summary>
/// Converts between wiki-relative paths (as agents and the UI use them) and full paths, and refuses any path that
/// would leave the wiki directory or reach into its git repository.
/// </summary>
internal sealed partial class WikiPaths(IOptions<WikiConfigurationSettings> settings)
{
    // The repository itself, and the files in the working tree that change what git versions.
    private static readonly string[] RepositoryNames = [".git", ".gitignore", ".gitattributes", ".gitmodules"];

    public string Root { get; } = Path.GetFullPath(settings.Value.WikiDirectory);

    public bool TryResolve(string path, out string fullPath)
    {
        fullPath = FullPath(path);
        return IsInsideWiki(fullPath) && !IsRepositoryPath(fullPath);
    }

    public string Resolve(string path)
    {
        string fullPath = FullPath(path);
        if (!IsInsideWiki(fullPath))
        {
            throw new InvalidOperationException($"The path '{path}' is outside the wiki. All paths must be relative to the wiki root.");
        }

        return IsRepositoryPath(fullPath)
            ? throw new InvalidOperationException($"The path '{path}' belongs to the wiki's version history (git), which cannot be read or changed. Use the wiki's pages instead.")
            : fullPath;
    }

    /// <summary>Whether a full path is in the wiki's git repository or is one of git's files, which listings must hide.</summary>
    public bool IsRepositoryPath(string fullPath) => Path
        .GetRelativePath(Root, fullPath)
        .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
        .Any(IsRepositoryName);

    /// <summary>
    /// Whether one segment of a path names git's repository or files. On Windows an 8.3 short name such as
    /// <c>GIT~1</c> reaches the same folder, so those are refused too.
    /// </summary>
    public static bool IsRepositoryName(string segment) =>
        RepositoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase)
        || (OperatingSystem.IsWindows() && ShortGitName().IsMatch(segment));

    public string ToRelative(string fullPath)
    {
        string relativePath = Path.GetRelativePath(Root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        return Directory.Exists(fullPath) ? $"{relativePath}/" : relativePath;
    }

    private string FullPath(string path) => Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? "." : path, Root);

    private bool IsInsideWiki(string fullPath) =>
        fullPath.Equals(Root, StringComparison.OrdinalIgnoreCase)
        || fullPath.StartsWith(Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^GIT[^~]{0,4}~\d+", RegexOptions.IgnoreCase)]
    private static partial Regex ShortGitName();
}
