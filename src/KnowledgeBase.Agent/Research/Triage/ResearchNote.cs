using System.Text;

using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Tools;

namespace KnowledgeBase.Agent.Research.Triage;

/// <summary>
/// The note triage writes for a topic: the research header <c>ResearchNoteHeader</c> reads, then each kept result's
/// source, the search engine's snippet, its kind, any summary and the exact quotes the Keeper chose. The page's text
/// never goes in beyond those length-capped quotes, as it may hold instructions aimed at the agents that read the note.
/// </summary>
/// <param name="FileName">
/// Derived from the topic and the search, so writing the note again after a restart names the same file.
/// </param>
internal sealed record ResearchNote(string FileName, string Content)
{
    /// <summary>
    /// The snippet is third-party text too, so like the Scout's quotes it is made one bounded line after a label: it
    /// cannot carry markdown structure or much of an instruction into the note.
    /// </summary>
    internal const int MaxSnippetLength = 300;

    /// <summary>A kind is one of the Keeper's options, but it is stored data, so it is bounded like the rest.</summary>
    private const int MaxKindLength = 40;

    public static ResearchNote Build(ResearchBatch batch, IReadOnlyList<BatchResult> kept)
    {
        StringBuilder content = new();
        content.AppendLine("---");
        content.AppendLine("origin: research");
        content.AppendLine($"topic: {batch.Topic}");
        content.AppendLine("sources:");
        foreach (BatchResult result in kept)
        {
            content.AppendLine($"  - {result.Result.Url}");
        }

        content.AppendLine("---");
        content.AppendLine();
        content.AppendLine($"# Research on {batch.Topic}");
        foreach (BatchResult result in kept)
        {
            Verdict verdict = result.Verdict!;
            content.AppendLine();
            content.AppendLine($"## {OneLine(result.Result.Title)}");
            string snippet = ScoutTools.Tidy(result.Result.Snippet, MaxSnippetLength);
            string?[] blocks =
            [
                $"Source: {result.Result.Url}",
                snippet.Length > 0 ? $"Search snippet: {snippet}" : null,
                string.IsNullOrWhiteSpace(verdict.Kind) ? null : $"Kind: {ScoutTools.Tidy(verdict.Kind, MaxKindLength)}",
                string.IsNullOrWhiteSpace(verdict.Summary) ? null : Plain(verdict.Summary),
                .. verdict.Quotes.Select(quote => $"> {quote}")
            ];
            foreach (string block in blocks.OfType<string>())
            {
                content.AppendLine();
                content.AppendLine(block);
            }
        }

        return new ResearchNote(
            $"research-{TopicPaths.Slug(batch.Topic)}-{batch.SearchedAt.UtcDateTime:yyyyMMdd-HHmmss}.md",
            content.ToString());
    }

    /// <summary>
    /// The Scout's words are model output derived from a web page, so a summary that opens with markdown syntax is
    /// escaped to stay a paragraph rather than becoming a heading, rule or list of the note's own.
    /// </summary>
    private static string Plain(string text) =>
        text.Length > 0 && "#>-*+|`=_~[!".Contains(text[0]) ? $"\\{text}" : text;

    private static string OneLine(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
