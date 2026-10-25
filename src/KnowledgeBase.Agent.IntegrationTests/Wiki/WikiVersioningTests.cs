using System.Diagnostics;

using KnowledgeBase.Agent.Files.Tools;
using KnowledgeBase.Agent.Notes.Commands;

namespace KnowledgeBase.Agent.IntegrationTests.Wiki;

/// <summary>
/// The wiki's git history with the real git CLI: the repository the app creates on start and the commits wiki-writing
/// runs leave behind.
/// </summary>
public sealed class WikiVersioningTests : IAsyncDisposable
{
    private const string NoteName = "note-2026-10-25-093000.md";
    private const string Collection = "feedback-from-susan";
    private const string PagePath = "people/susan.md";

    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task AnExistingWikiIsStartedForTheFirstTime()
    {
        string page = _wiki.Write(Path.Combine(_wiki.Settings.WikiDirectory, "projects"), "garden.md", "# Garden\n");

        await _wiki.InitialiseAsync();
        string history = await GitAsync("log", "--format=%an|%s");
        string trackedFiles = await GitAsync("ls-files");

        ItShouldMakeOnlyTheWikiARepository();
        ItShouldCommitTheExistingPagesOnce();
        ItShouldNotTrackPipelineFiles();

        void ItShouldMakeOnlyTheWikiARepository()
        {
            Directory.Exists(Path.Combine(_wiki.Settings.WikiDirectory, ".git")).ShouldBeTrue();
            Path.Exists(Path.Combine(_wiki.Settings.RootDirectory, ".git")).ShouldBeFalse();
        }

        void ItShouldCommitTheExistingPagesOnce()
        {
            history.ShouldBe("Knowledge Base|Initial wiki");
            trackedFiles.Split('\n').ShouldContain("projects/garden.md");
        }

        void ItShouldNotTrackPipelineFiles() => trackedFiles.ShouldNotContain("notes/", Case.Sensitive);
    }

    [Test]
    public async Task TheAppRestarts()
    {
        await _wiki.InitialiseAsync();

        await _wiki.InitialiseAsync();
        string history = await GitAsync("log", "--format=%s");

        ItShouldKeepTheExistingHistory();

        void ItShouldKeepTheExistingHistory() => history.ShouldBe("Initial wiki");
    }

    [Test]
    public async Task TheThreadWeaverWeavesANote()
    {
        _wiki.FactFinderWritesFacts(Collection);
        _wiki.ThreadWeaver.Behaviour = async (_, cancellationToken) =>
        {
            await _wiki.CallToolAsync(
                nameof(FileTools.CreateFile),
                PagePath,
                () => _wiki.Get<FileTools>().CreateFile(PagePath, "# Susan\n\nAsked for clearer trade-offs.\n", cancellationToken: cancellationToken));
            return "Filed Susan's feedback on the design review.";
        };
        await StartAsync();

        await CaptureAsync();
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).Count == 1 && await _wiki.RunsFinishedAsync(2));
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        string history = await GitAsync("log", "--format=%an|%s");
        string lastCommit = await GitAsync("show", "--name-only", "--format=%B", "HEAD");

        ItShouldFinish();
        ItShouldCommitTheRunAsTheThreadWeaver();
        ItShouldCommitThePageTheRunCreated();

        void ItShouldFinish() => settled.ShouldBeTrue(pipeline);

        void ItShouldCommitTheRunAsTheThreadWeaver() => history.Split('\n').ShouldBe(
        [
            "ThreadWeaver|ThreadWeaver: Filed Susan's feedback on the design review.",
            "Knowledge Base|Initial wiki"
        ]);

        void ItShouldCommitThePageTheRunCreated()
        {
            lastCommit.ShouldContain("Status: Succeeded", Case.Sensitive);
            lastCommit.ShouldContain($"Trigger: Facts {Collection}.json", Case.Sensitive);
            lastCommit.Split('\n').ShouldContain(PagePath);
        }
    }

    [Test]
    public async Task TheThreadWeaverChangesNothing()
    {
        _wiki.FactFinderWritesFacts(Collection);
        await StartAsync();

        await CaptureAsync();
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).Count == 1 && await _wiki.RunsFinishedAsync(2));
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        string history = await GitAsync("log", "--format=%s");

        ItShouldFinish();
        ItShouldNotMakeAnEmptyCommit();

        void ItShouldFinish() => settled.ShouldBeTrue(pipeline);

        void ItShouldNotMakeAnEmptyCommit() => history.ShouldBe("Initial wiki");
    }

    private async Task StartAsync()
    {
        await _wiki.InitialiseAsync();
        _wiki.Start();
    }

    private async Task CaptureAsync()
    {
        Result<string, Error> captured = await _wiki.Get<CaptureWikiNoteCommandHandler>()
            .Handle(new CaptureWikiNoteCommand(Path.GetFileNameWithoutExtension(NoteName), "Susan asked for clearer trade-offs."), CancellationToken.None);
        captured.IsSuccess.ShouldBeTrue();
    }

    /// <summary>Reads the wiki repository the way the author would, from the git CLI.</summary>
    private async Task<string> GitAsync(params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git") { RedirectStandardOutput = true, UseShellExecute = false };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(_wiki.Settings.WikiDirectory);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.Trim();
    }
}