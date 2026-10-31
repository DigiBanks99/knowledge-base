using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Files.Tools;

/// <summary>
/// What a run may do with the file tools, given where the content it works on came from. Research is web content,
/// which may carry injected instructions, so a run over it is refused anything that removes or restructures pages and
/// anything that writes into or reads from the author's personal sections, whatever the model was told. Reads matter
/// as much as writes: what a run copies into a topic page can reach the Pleb's search queries, which leave the machine.
/// </summary>
internal static class RunToolPolicy
{
    private static readonly string[] PersonalSections =
        [.. WikiSections.All.Where(section => section.Personal).Select(section => section.Folder)];

    private static readonly string PersonalSectionList =
        string.Join(" and ", PersonalSections.Select(folder => $"`{folder}/`"));

    private static readonly string[] RemovingTools = [nameof(FileTools.DeleteFile), nameof(FileTools.MoveFile)];

    private static readonly string[] WritingTools =
        [nameof(FileTools.CreateDirectory), nameof(FileTools.CreateFile), nameof(FileTools.PatchFile)];

    private static readonly string[] ReadingTools = [nameof(FileTools.ReadFile)];

    /// <summary>
    /// Why the tool call is refused, phrased for the agent so it can continue with what it may do, or <c>null</c> when
    /// the call is allowed.
    /// </summary>
    /// <param name="origin">The origin of the run making the call.</param>
    /// <param name="tool">The name of the file tool being called.</param>
    /// <param name="wikiPaths">The paths the call acts on, relative to the wiki root.</param>
    public static string? Refusal(FactOrigin origin, string tool, IReadOnlyList<string> wikiPaths)
    {
        if (origin != FactOrigin.Research)
        {
            return null;
        }

        if (RemovingTools.Contains(tool, StringComparer.Ordinal))
        {
            return $"`{tool}` is not allowed while weaving research facts. Research is a third-party claim and never a "
                + "reason to remove, move or restructure existing pages. Add the research to existing pages with "
                + "`PatchFile`, or create a new page with `CreateFile`, instead.";
        }

        string? personalPath = wikiPaths.FirstOrDefault(IsPersonal);
        if (personalPath is null)
        {
            return null;
        }

        string? guidance =
            WritingTools.Contains(tool, StringComparer.Ordinal)
                ? "never hold research. File the research under the wiki topic it was gathered for instead."
            : ReadingTools.Contains(tool, StringComparer.Ordinal)
                ? "research never reads them. Read the pages of the wiki topic the research was gathered for instead."
            : null;
        return guidance is null
            ? null
            : $"`{tool}` on '{personalPath}' is not allowed while weaving research facts: {PersonalSectionList} are "
                + $"the author's personal sections and {guidance}";
    }

    /// <summary>
    /// Whether a listing or search in a run of the given origin leaves the path out. Research runs never see the
    /// personal sections; leaving them out rather than refusing the call keeps the rest of the result usable.
    /// </summary>
    /// <param name="origin">The origin of the run making the call.</param>
    /// <param name="wikiPath">A path in the result, relative to the wiki root.</param>
    public static bool Hides(FactOrigin origin, string wikiPath) => origin == FactOrigin.Research && IsPersonal(wikiPath);

    private static bool IsPersonal(string wikiPath)
    {
        string firstSegment = wikiPath
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(segment => segment != ".")
            ?? string.Empty;
        return PersonalSections.Contains(firstSegment, StringComparer.OrdinalIgnoreCase);
    }
}
