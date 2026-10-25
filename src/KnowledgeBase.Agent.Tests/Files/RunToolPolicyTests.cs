using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Files.Tools;

namespace KnowledgeBase.Agent.Tests.Files;

public sealed class RunToolPolicyTests
{
    [Test]
    [Arguments(nameof(FileTools.DeleteFile), new[] { "projects/knowledge-base/index.md" })]
    [Arguments(nameof(FileTools.DeleteFile), new[] { "people/jane-doe/index.md" })]
    [Arguments(nameof(FileTools.MoveFile), new[] { "projects/knowledge-base/index.md", "projects/archive/index.md" })]
    [Arguments(nameof(FileTools.MoveFile), new[] { "learning/agents.md", "people/jane-doe/agents.md" })]
    public void ARunOverResearchRemovesOrRestructuresAPage(string tool, string[] paths)
    {
        string? refusal = RunToolPolicy.Refusal(FactOrigin.Research, tool, paths);

        ItShouldBeRefused();
        ItShouldExplainThatResearchNeverRemovesOrRestructures();

        void ItShouldBeRefused() => refusal.ShouldNotBeNull();

        void ItShouldExplainThatResearchNeverRemovesOrRestructures() => refusal.ShouldNotBeNull().ShouldSatisfyAllConditions(
            message => message.ShouldStartWith($"`{tool}` is not allowed while weaving research facts.", Case.Sensitive),
            message => message.ShouldContain("never a reason to remove, move or restructure", Case.Sensitive),
            message => message.ShouldContain("`PatchFile`", Case.Sensitive));
    }

    [Test]
    [Arguments(nameof(FileTools.CreateFile), "people/jane-doe/index.md")]
    [Arguments(nameof(FileTools.PatchFile), "me/goals.md")]
    [Arguments(nameof(FileTools.CreateDirectory), "people/new-person/")]
    [Arguments(nameof(FileTools.CreateFile), "PEOPLE/Jane-Doe/index.md")]
    [Arguments(nameof(FileTools.PatchFile), "./me/index.md")]
    [Arguments(nameof(FileTools.CreateDirectory), "me")]
    public void ARunOverResearchWritesIntoAPersonalSection(string tool, string path)
    {
        string? refusal = RunToolPolicy.Refusal(FactOrigin.Research, tool, [path]);

        ItShouldBeRefused();
        ItShouldNameThePathAndThePersonalSections();

        void ItShouldBeRefused() => refusal.ShouldNotBeNull();

        void ItShouldNameThePathAndThePersonalSections() => refusal.ShouldNotBeNull().ShouldSatisfyAllConditions(
            message => message.ShouldStartWith($"`{tool}` on '{path}' is not allowed while weaving research facts", Case.Sensitive),
            message => message.ShouldContain("`me/` and `people/` are the author's personal sections", Case.Sensitive),
            message => message.ShouldContain("under the wiki topic it was gathered for", Case.Sensitive));
    }

    [Test]
    [Arguments(nameof(FileTools.CreateFile), "projects/knowledge-base/agent-framework.md")]
    [Arguments(nameof(FileTools.PatchFile), "learning/agents.md")]
    [Arguments(nameof(FileTools.CreateDirectory), "projects/knowledge-base/research/")]
    [Arguments(nameof(FileTools.PatchFile), "projects/meetings/people.md")]
    [Arguments(nameof(FileTools.CreateFile), "learning/memes.md")]
    [Arguments(nameof(FileTools.ReadFile), "people/jane-doe/index.md")]
    [Arguments(nameof(FileTools.ListFiles), "me/")]
    [Arguments(nameof(FileTools.SearchWiki), null)]
    public void ARunOverResearchAddsToATopic(string tool, string? path)
    {
        string? refusal = RunToolPolicy.Refusal(FactOrigin.Research, tool, path is null ? [] : [path]);

        ItShouldBeAllowed();

        void ItShouldBeAllowed() => refusal.ShouldBeNull();
    }

    [Test]
    [Arguments(nameof(FileTools.DeleteFile), new[] { "people/jane-doe/index.md" })]
    [Arguments(nameof(FileTools.MoveFile), new[] { "me/goals.md", "me/archive/goals.md" })]
    [Arguments(nameof(FileTools.CreateFile), new[] { "people/jane-doe/index.md" })]
    [Arguments(nameof(FileTools.PatchFile), new[] { "me/goals.md" })]
    [Arguments(nameof(FileTools.CreateDirectory), new[] { "people/jane-doe/" })]
    public void ARunOverTheAuthorsNoteChangesAnyPage(string tool, string[] paths)
    {
        string? refusal = RunToolPolicy.Refusal(FactOrigin.Author, tool, paths);

        ItShouldBeAllowed();

        void ItShouldBeAllowed() => refusal.ShouldBeNull();
    }
}
