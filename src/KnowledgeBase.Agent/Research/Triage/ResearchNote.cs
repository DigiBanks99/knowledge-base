using System.Text;

using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Triage;

/// <summary>
/// The note triage writes for a topic: the research header <c>ResearchNoteHeader</c> reads, then each kept result's
/// source and the Scout's own summary and quotes. Page text never goes in, as it may hold instructions aimed at the
/// agents that read the note.
/// </summary>
/// <param name="FileName">
/// Derived from the topic and the search, so writing the note again after a restart names the same file.
/// </param>
internal sealed record ResearchNote(string FileName, string Content)
{
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
            ScoutVerdict verdict = result.Verdict!;
            content.AppendLine();
            content.AppendLine($"## {OneLine(result.Result.Title)}");
            content.AppendLine();
            content.AppendLine($"Source: {result.Result.Url}");
            content.AppendLine();
            content.AppendLine(Plain(verdict.Summary!));
            foreach (string quote in verdict.Quotes)
            {
                content.AppendLine();
                content.AppendLine($"> {quote}");
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
