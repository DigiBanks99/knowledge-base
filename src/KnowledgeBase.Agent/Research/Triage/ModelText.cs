namespace KnowledgeBase.Agent.Research.Triage;

/// <summary>Text from a model or the web on its way into a note the pipeline reads.</summary>
internal static class ModelText
{
    /// <summary>
    /// Makes the text one line of at most <paramref name="limit"/> characters, so it cannot carry markdown structure or
    /// a header of its own into the note.
    /// </summary>
    public static string Tidy(string? text, int limit)
    {
        string line = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return line.Length <= limit ? line : $"{line[..(limit - 1)].TrimEnd()}…";
    }
}
