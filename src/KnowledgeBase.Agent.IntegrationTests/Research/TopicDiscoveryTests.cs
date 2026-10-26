using KnowledgeBase.Agent.Files.Tools;
using KnowledgeBase.Agent.Notes.Commands;

namespace KnowledgeBase.Agent.IntegrationTests.Research;

/// <summary>Research topics derived from the wiki as the app starts and as the ThreadWeaver files notes.</summary>
public sealed class TopicDiscoveryTests : IAsyncDisposable
{
    private const string Collection = "garden-plans";

    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    private string ProjectsDirectory => Path.Combine(_wiki.Settings.WikiDirectory, "projects");

    [Test]
    public async Task AnExistingWikiIsStartedForTheFirstTime()
    {
        _wiki.Write(Path.Combine(ProjectsDirectory, "garden"), "index.md", "# Garden\n");
        _wiki.Write(Path.Combine(_wiki.Settings.WikiDirectory, "learning"), "rust.md", "# Rust\n");
        _wiki.Write(Path.Combine(_wiki.Settings.WikiDirectory, "people", "jane-doe"), "index.md", "# Jane Doe\n");

        await StartAsync();

        ItShouldKeepStateForEveryTopic();
        ItShouldRequestCurationForEveryTopic();

        void ItShouldKeepStateForEveryTopic() => TestWiki.FileNames(_wiki.Settings.ResearchTopicsDirectory)
            .ShouldBe(["learning--rust.md.json", "projects--garden.json"], ignoreOrder: true);

        void ItShouldRequestCurationForEveryTopic() => TestWiki.FileNames(_wiki.Settings.CurationRequestsDirectory)
            .ShouldBe(["learning--rust.md.json", "projects--garden.json"], ignoreOrder: true);
    }

    [Test]
    public async Task TheThreadWeaverStartsANewProject()
    {
        _wiki.FactFinderWritesFacts(Collection);
        _wiki.ThreadWeaver.Behaviour = async (_, cancellationToken) =>
        {
            await _wiki.CallToolAsync(
                nameof(FileTools.CreateFile),
                "projects/garden/index.md",
                () => _wiki.Get<FileTools>().CreateFile("projects/garden/index.md", "# Garden\n", cancellationToken: cancellationToken));
            return "Started the garden project.";
        };
        await StartAsync();

        await CaptureAsync();
        bool discovered = await Eventually.Until(() =>
            TestWiki.FileNames(_wiki.Settings.CurationRequestsDirectory).Contains("projects--garden.json"));
        string pipeline = discovered ? string.Empty : await _wiki.DescribeAsync();

        ItShouldRequestCurationForTheNewTopic();
        ItShouldKeepStateForIt();

        void ItShouldRequestCurationForTheNewTopic() => discovered.ShouldBeTrue(pipeline);

        void ItShouldKeepStateForIt() =>
            TestWiki.FileNames(_wiki.Settings.ResearchTopicsDirectory).ShouldBe(["projects--garden.json"]);
    }

    [Test]
    public async Task TheThreadWeaverDeletesATopicsOnlyPage()
    {
        _wiki.Write(Path.Combine(_wiki.Settings.WikiDirectory, "learning"), "rust.md", "# Rust\n");
        _wiki.FactFinderWritesFacts(Collection);
        _wiki.ThreadWeaver.Behaviour = async (_, _) =>
        {
            await _wiki.CallToolAsync(
                nameof(FileTools.DeleteFile),
                "learning/rust.md",
                () => _wiki.Get<FileTools>().DeleteFile("learning/rust.md"));
            return "Removed the Rust page.";
        };
        await StartAsync();

        await CaptureAsync();
        bool pruned = await Eventually.Until(() => TestWiki.FileNames(_wiki.Settings.ResearchTopicsDirectory).Count == 0);
        string pipeline = pruned ? string.Empty : await _wiki.DescribeAsync();

        ItShouldPruneTheTopic();
        ItShouldDropItsCurationRequest();

        void ItShouldPruneTheTopic() => pruned.ShouldBeTrue(pipeline);

        void ItShouldDropItsCurationRequest() => TestWiki.FileNames(_wiki.Settings.CurationRequestsDirectory).ShouldBeEmpty();
    }

    [Test]
    public async Task TheThreadWeaverDeletesEveryPageOfAFolderTopic()
    {
        _wiki.Write(Path.Combine(ProjectsDirectory, "garden"), "index.md", "# Garden\n");
        _wiki.FactFinderWritesFacts(Collection);
        _wiki.ThreadWeaver.Behaviour = async (_, _) =>
        {
            await _wiki.CallToolAsync(
                nameof(FileTools.DeleteFile),
                "projects/garden/index.md",
                () => _wiki.Get<FileTools>().DeleteFile("projects/garden/index.md"));
            return "Removed the garden project.";
        };
        await StartAsync();

        await CaptureAsync();
        bool pruned = await Eventually.Until(() => TestWiki.FileNames(_wiki.Settings.ResearchTopicsDirectory).Count == 0);
        string pipeline = pruned ? string.Empty : await _wiki.DescribeAsync();

        ItShouldPruneTheTopicThoughItsFolderIsLeft();

        void ItShouldPruneTheTopicThoughItsFolderIsLeft()
        {
            pruned.ShouldBeTrue(pipeline);
            Directory.Exists(Path.Combine(ProjectsDirectory, "garden")).ShouldBeTrue();
        }
    }

    private async Task StartAsync()
    {
        await _wiki.InitialiseAsync();
        _wiki.Start();
    }

    private async Task CaptureAsync()
    {
        Result<string, Error> captured = await _wiki.Get<CaptureWikiNoteCommandHandler>()
            .Handle(new CaptureWikiNoteCommand("note-2026-10-25-093000", "Plant tomatoes in spring."), CancellationToken.None);
        captured.IsSuccess.ShouldBeTrue();
    }
}
