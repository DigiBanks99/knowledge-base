using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Files.Tools;

/// <summary>
/// What a run may do with the file tools, given where the content it works on came from. Research is web content,
/// which may carry injected instructions, so a run over it is refused anything that removes or restructures pages and
/// anything that writes into the author's personal sections, whatever the model was told.
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
        return WritingTools.Contains(tool, StringComparer.Ordinal) && personalPath is not null
            ? $"`{tool}` on '{personalPath}' is not allowed while weaving research facts: {PersonalSectionList} are "
                + "the author's personal sections and never hold research. File the research under the wiki topic it "
                + "was gathered for instead."
            : null;
    }

    private static bool IsPersonal(string wikiPath)
    {
        string firstSegment = wikiPath
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(segment => segment != ".")
            ?? string.Empty;
        return PersonalSections.Contains(firstSegment, StringComparer.OrdinalIgnoreCase);
    }
}
