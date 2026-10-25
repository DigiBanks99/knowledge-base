using KnowledgeBase.Agent.Facts.Models;

namespace KnowledgeBase.Agent.Notes;

/// <summary>Where a note came from: the author, or research on a wiki topic gathered from the given sources.</summary>
public sealed record NoteProvenance(FactOrigin Origin, string? Topic, IReadOnlyList<string> Sources)
{
    public static readonly NoteProvenance Author = new(FactOrigin.Author, null, []);
}

public sealed class MalformedResearchHeaderError(string problem)
    : ValidationError("MalformedResearchHeader", $"The note's research header is malformed: {problem}");

/// <summary>
/// Reads the header a research note starts with:
/// <code>
/// ---
/// origin: research
/// topic: projects/knowledge-base
/// sources:
///   - https://example.com/article
/// ---
/// </code>
/// A note without one is the author's own. Notes may start with a horizontal rule, so the header is only recognised
/// when <c>origin:</c> (in any case) directly follows the opening <c>---</c>; once recognised, anything unexpected
/// fails the note rather than letting research content pass as the author's. Research notes are written by the
/// pipeline itself, which always puts <c>origin</c> first.
/// </summary>
public static class ResearchNoteHeader
{
    private const string Delimiter = "---";
    private const string OriginKey = "origin";
    private const string TopicKey = "topic";
    private const string SourcesKey = "sources";

    public static Result<NoteProvenance, Error> Parse(string content)
    {
        string[] lines = content.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        if (lines.Length < 2 || lines[0].Trim() != Delimiter
            || !lines[1].TrimStart().StartsWith($"{OriginKey}:", StringComparison.OrdinalIgnoreCase))
        {
            return NoteProvenance.Author;
        }

        int closing = Array.FindIndex(lines, 1, line => line.Trim() == Delimiter);
        if (closing < 0)
        {
            return Malformed($"it is never closed with a `{Delimiter}` line.");
        }

        Dictionary<string, string> values = [];
        List<string> sources = [];
        string? currentKey = null;
        foreach (string line in lines[1..closing].Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('-'))
            {
                if (currentKey != SourcesKey)
                {
                    return Malformed($"the list item `{trimmed}` must be under `{SourcesKey}:`.");
                }

                sources.Add(trimmed[1..].Trim());
                continue;
            }

            int colon = trimmed.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return Malformed($"the line `{trimmed}` is not a `key: value` pair.");
            }

            string key = trimmed[..colon].Trim().ToLowerInvariant();
            string value = trimmed[(colon + 1)..].Trim();
            if (key is not (OriginKey or TopicKey or SourcesKey))
            {
                return Malformed($"`{key}` is not a known key; use `{OriginKey}`, `{TopicKey}` and `{SourcesKey}`.");
            }

            if (!values.TryAdd(key, value))
            {
                return Malformed($"`{key}` appears more than once.");
            }

            if (key == SourcesKey && value.Length > 0)
            {
                return Malformed($"list each source on its own line under `{SourcesKey}:` as `  - <url>`.");
            }

            currentKey = key;
        }

        return values[OriginKey].ToLowerInvariant() switch
        {
            "author" => Author(values),
            "research" => Research(values.GetValueOrDefault(TopicKey), sources),
            string origin => Malformed($"`{OriginKey}` must be `research` or `author`, not `{origin}`."),
        };
    }

    private static Result<NoteProvenance, Error> Author(Dictionary<string, string> values) =>
        values.Count > 1
            ? Malformed($"an `author` note has no `{TopicKey}` or `{SourcesKey}`.")
            : NoteProvenance.Author;

    private static Result<NoteProvenance, Error> Research(string? topic, List<string> sources)
    {
        if (string.IsNullOrEmpty(topic))
        {
            return Malformed($"a research note needs a `{TopicKey}`.");
        }

        // Checked by hand rather than with Path.IsPathRooted, so `C:\...` is refused on Linux as well.
        if (topic.StartsWith('/') || topic.StartsWith('\\') || topic.Contains(':', StringComparison.Ordinal)
            || topic.Split('/', '\\').Contains(".."))
        {
            return Malformed($"the topic `{topic}` must be a path relative to the wiki root.");
        }

        if (sources.Count == 0)
        {
            return Malformed($"a research note needs at least one source under `{SourcesKey}:`.");
        }

        string? invalid = sources.FirstOrDefault(source =>
            !Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps));
        return invalid is not null
            ? Malformed($"the source `{invalid}` is not an absolute http(s) URL.")
            : new NoteProvenance(FactOrigin.Research, topic, sources);
    }

    private static Result<NoteProvenance, Error> Malformed(string problem) => new MalformedResearchHeaderError(problem);
}
