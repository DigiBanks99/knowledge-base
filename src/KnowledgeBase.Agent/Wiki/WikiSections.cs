namespace KnowledgeBase.Agent.Wiki;

public sealed record WikiSection(string Folder, string Title, string Purpose);

/// <summary>
/// The top-level sections of the wiki, seeded with an index page on startup so agents always find a structure to
/// file into. Keep in sync with the folders described in the wiki rules.
/// </summary>
public static class WikiSections
{
    public static IReadOnlyList<WikiSection> All { get; } =
    [
        new("me", "Me", "The author's own growth, feedback received, goals and achievements."),
        new("people", "People", "Anyone with a relationship to the author."),
        new("projects", "Projects", "Initiatives with a defined scope and goal."),
        new("learning", "Learning", "Knowledge gained or referenced, both journaled learning and factual recall."),
        new("plans", "Plans", "Time-boxed intentions, weekly or otherwise."),
        new("decisions", "Decisions", "Choices made or being weighed, with rationale."),
        new("reflections", "Reflections", "Processing an experience or feeling.")
    ];

    public static string RootIndex() =>
        $"""
         # Wiki

         The root of the knowledge wiki.

         ## Sections

         {string.Join('\n', All.Select(section => $"- [{section.Title}]({section.Folder}/index.md): {section.Purpose}"))}
         """.ReplaceLineEndings("\n");

    public static string SectionIndex(WikiSection section) =>
        $"""
         # {section.Title}

         {section.Purpose}

         [Back to the wiki root](../index.md)

         ## Pages

         _No pages yet._
         """.ReplaceLineEndings("\n");
}