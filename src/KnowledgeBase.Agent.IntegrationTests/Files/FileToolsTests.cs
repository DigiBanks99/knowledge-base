using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Files.Tools;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace KnowledgeBase.Agent.IntegrationTests.Files;

/// <summary>
/// The ThreadWeaver's file tools inside a run, called the way the agent calls them, over a wiki with a personal page
/// and a topic page that both mention the same term.
/// </summary>
public sealed class FileToolsTests : IAsyncDisposable
{
    private const string Topic = "projects/knowledge-base";
    private const string TopicPage = "projects/knowledge-base/index.md";
    private const string PersonalPage = "people/jane-doe/index.md";
    private const string Term = "zanzibar";

    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task AResearchRunReadsAPersonalPage()
    {
        await LayOutTheWikiAsync();

        (Exception? exception, AgentRun run) = await InRunAsync(FactOrigin.Research, tools =>
            Catch.ExceptionAsync(() => _wiki.CallToolAsync(nameof(FileTools.ReadFile), PersonalPage, () => tools.ReadFile(PersonalPage))));

        ItShouldBeRefused();
        ItShouldRecordTheRefusalOnTheRun();

        void ItShouldBeRefused() => exception.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("research never reads them", Case.Sensitive);

        void ItShouldRecordTheRefusalOnTheRun() => run.ToolCalls.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            call => call.Tool.ShouldBe(nameof(FileTools.ReadFile)),
            call => call.Target.ShouldBe(PersonalPage),
            call => call.Succeeded.ShouldBeFalse(),
            call => call.Error.ShouldNotBeNull().ShouldContain("`me/` and `people/` are the author's personal sections", Case.Sensitive));
    }

    [Test]
    public async Task AResearchRunReadsATopicPage()
    {
        await LayOutTheWikiAsync();

        (string content, AgentRun run) = await InRunAsync(FactOrigin.Research, tools =>
            _wiki.CallToolAsync(nameof(FileTools.ReadFile), TopicPage, () => tools.ReadFile(TopicPage)));

        ItShouldReturnThePage();

        void ItShouldReturnThePage()
        {
            content.ShouldContain($"The topic mentions {Term}.", Case.Sensitive);
            run.ToolCalls.ShouldHaveSingleItem().Succeeded.ShouldBeTrue();
        }
    }

    [Test]
    public async Task AResearchRunSearchesTheWiki()
    {
        await LayOutTheWikiAsync();

        (IReadOnlyList<string> matches, _) = await InRunAsync(FactOrigin.Research, tools =>
            _wiki.CallToolAsync(nameof(FileTools.SearchWiki), Term, () => tools.SearchWiki(Term)));

        ItShouldFindTheTopicPage();
        ItShouldLeaveOutThePersonalSections();

        void ItShouldFindTheTopicPage() => matches.ShouldContain(match => match.StartsWith($"{TopicPage}:", StringComparison.Ordinal));

        void ItShouldLeaveOutThePersonalSections() => matches.ShouldNotContain(match => IsPersonal(match));
    }

    [Test]
    public async Task AResearchRunListsTheWikiRoot()
    {
        await LayOutTheWikiAsync();

        (IReadOnlyList<string> entries, _) = await InRunAsync(FactOrigin.Research, tools =>
            _wiki.CallToolAsync(nameof(FileTools.ListFiles), ".", () => Task.FromResult<IReadOnlyList<string>>([.. tools.ListFiles(".")])));

        ItShouldListTheTopicSections();
        ItShouldLeaveOutThePersonalSections();

        void ItShouldListTheTopicSections() => entries.ShouldContain(entry => entry.StartsWith("projects", StringComparison.Ordinal));

        void ItShouldLeaveOutThePersonalSections() => entries.ShouldNotContain(entry => IsPersonal(entry));
    }

    [Test]
    [Arguments("people/")]
    [Arguments("people/jane-doe/")]
    [Arguments("people/nobody-the-author-knows/")]
    public async Task AResearchRunListsAPersonalSection(string directory)
    {
        await LayOutTheWikiAsync();

        (IReadOnlyList<string> entries, AgentRun run) = await InRunAsync(FactOrigin.Research, tools =>
            _wiki.CallToolAsync(nameof(FileTools.ListFiles), directory, () => Task.FromResult<IReadOnlyList<string>>([.. tools.ListFiles(directory)])));

        ItShouldListNothing();

        void ItShouldListNothing()
        {
            entries.ShouldBeEmpty();
            run.ToolCalls.ShouldHaveSingleItem().Succeeded.ShouldBeTrue();
        }
    }

    [Test]
    public async Task AnAuthorRunReadsSearchesAndListsThePersonalSections()
    {
        await LayOutTheWikiAsync();

        ((string content, IReadOnlyList<string> matches, IReadOnlyList<string> entries), AgentRun run) = await InRunAsync(FactOrigin.Author, async tools =>
        {
            string read = await _wiki.CallToolAsync(nameof(FileTools.ReadFile), PersonalPage, () => tools.ReadFile(PersonalPage));
            IReadOnlyList<string> found = await _wiki.CallToolAsync(nameof(FileTools.SearchWiki), Term, () => tools.SearchWiki(Term));
            IReadOnlyList<string> listed = await _wiki.CallToolAsync(
                nameof(FileTools.ListFiles), "people/", () => Task.FromResult<IReadOnlyList<string>>([.. tools.ListFiles("people/")]));
            return (read, found, listed);
        });

        ItShouldReadThePersonalPage();
        ItShouldFindThePersonalPage();
        ItShouldListThePersonalSection();
        ItShouldAllowEveryCall();

        void ItShouldReadThePersonalPage() => content.ShouldContain($"Jane mentioned {Term}.", Case.Sensitive);

        void ItShouldFindThePersonalPage() => matches.ShouldContain(match => match.StartsWith($"{PersonalPage}:", StringComparison.Ordinal));

        void ItShouldListThePersonalSection() => entries.ShouldContain(entry => entry.StartsWith("people/jane-doe", StringComparison.Ordinal));

        void ItShouldAllowEveryCall() => run.ToolCalls.ShouldAllBe(call => call.Succeeded);
    }

    private static bool IsPersonal(string wikiPath) =>
        wikiPath.Split(['/', '\\', ':'])[0] is "me" or "people";

    private async Task LayOutTheWikiAsync()
    {
        await _wiki.InitialiseAsync();
        WriteWikiPage(TopicPage, $"# Knowledge base\n\nThe topic mentions {Term}.\n");
        WriteWikiPage(PersonalPage, $"# Jane Doe\n\nJane mentioned {Term}.\n");
    }

    /// <summary>Runs the ThreadWeaver over content of the given origin, with <paramref name="call"/> as what it does.</summary>
    private async Task<(T Result, AgentRun Run)> InRunAsync<T>(FactOrigin origin, Func<FileTools, Task<T>> call)
    {
        FileTools tools = _wiki.Get<FileTools>();
        T result = default!;
        Result<AgentRun, Error> run = await _wiki.Get<AgentRunTracker>().RunAsync(
            KnownAgents.ThreadWeaver,
            new AgentRunTrigger(AgentRunTriggerKind.Facts, "facts.json"),
            origin,
            origin == FactOrigin.Research ? Topic : null,
            async _ =>
            {
                result = await call(tools);
                return new AgentResponse(new ChatMessage(ChatRole.Assistant, "Done."));
            },
            CancellationToken.None);
        return (result, run.Value);
    }

    private void WriteWikiPage(string path, string content)
    {
        string fullPath = Path.Combine(_wiki.Settings.WikiDirectory, path);
        _wiki.Write(Path.GetDirectoryName(fullPath)!, Path.GetFileName(fullPath), content);
    }
}
