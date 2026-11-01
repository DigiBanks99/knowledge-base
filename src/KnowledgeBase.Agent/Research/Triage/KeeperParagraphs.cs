using System.Text.RegularExpressions;

namespace KnowledgeBase.Agent.Research.Triage;

/// <summary>
/// The prose paragraphs of a fetched page, which the Keeper reads numbered and picks its quotes from. Headings, short
/// fragments and lines holding only images, badges or links are left out first, so a badge's URL (a base64 shields.io
/// image was offered as a quote in the spike) never reaches the model as a paragraph.
/// </summary>
internal static partial class KeeperParagraphs
{
    /// <summary>Anything shorter is a fragment (a caption, a button label, a date), not a paragraph worth quoting.</summary>
    internal const int MinLength = 40;

    public static IReadOnlyList<string> From(string markdown)
    {
        IEnumerable<string> lines = markdown.ReplaceLineEndings("\n").Split('\n').Where(line => !IsDecoration(line));
        return
        [
            .. string.Join('\n', lines)
                .Split("\n\n")
                .Select(block => string.Join(' ', block.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
                .Where(block => block.Length >= MinLength && !block.StartsWith('#'))
        ];
    }

    /// <summary>
    /// Whether a line is a heading, a bare URL, or nothing but images, links (a badge is a link around an image) and
    /// separators.
    /// Images go first so a badge's outer link is left empty and goes next.
    /// </summary>
    private static bool IsDecoration(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        // A heading straight above its text, with no blank line between, would otherwise take the paragraph with it.
        if (trimmed.StartsWith('#') || BareUrl().IsMatch(trimmed))
        {
            return true;
        }

        string rest = Link().Replace(Image().Replace(trimmed, string.Empty), string.Empty);
        return rest.Length < trimmed.Length && Separators().IsMatch(rest);
    }

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"^[\s|•·,\-–—*]*$")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^<?https?://\S+>?$")]
    private static partial Regex BareUrl();
}
