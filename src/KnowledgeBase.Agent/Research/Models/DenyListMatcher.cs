using System.Text.RegularExpressions;

namespace KnowledgeBase.Agent.Research.Models;

/// <summary>
/// Answers whether a text, such as a search query, contains a denied name: case-insensitively and as whole words, so
/// <c>Ann</c> is found in "ask ann about it" but not in "annual report". The words of a name may be separated by
/// spaces, hyphens or underscores in the text.
/// </summary>
public sealed partial class DenyListMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly (string Name, Regex Pattern)[] _names;

    public DenyListMatcher(IEnumerable<string> deniedNames) => _names =
    [
        .. deniedNames
            .Select(name => (Name: name, Words: Words(name)))
            .Where(name => name.Words.Length > 0)
            .Select(name => (name.Name, new Regex(
                $@"(?<![\p{{L}}\p{{N}}]){string.Join(@"[\s_-]+", name.Words.Select(Regex.Escape))}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                MatchTimeout)))
    ];

    /// <summary>The first denied name the text contains, as the deny-list spells it.</summary>
    public Maybe<string> FindDeniedName(string text) => _names
        .Where(name => name.Pattern.IsMatch(text))
        .Select(name => name.Name)
        .TryFirst();

    internal static string[] Words(string name) => WordSeparators().Split(name.Trim()).Where(word => word.Length > 0).ToArray();

    [GeneratedRegex(@"[\s_-]+")]
    private static partial Regex WordSeparators();
}
