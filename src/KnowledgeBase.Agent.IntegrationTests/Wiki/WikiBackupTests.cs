using System.Diagnostics;

using KnowledgeBase.Agent.Files.Tools;
using KnowledgeBase.Agent.Notes.Commands;
using KnowledgeBase.Agent.Wiki.Versioning;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.IntegrationTests.Wiki;

/// <summary>
/// Pushing the wiki's history to a backup remote with the real git CLI. The remote is a bare repository on disk, so
/// no SSH or network is involved; the SSH options are covered by the unit tests.
/// </summary>
public sealed class WikiBackupTests : IAsyncDisposable
{
    private const string NoteName = "note-2026-10-25-093000.md";
    private const string Collection = "feedback-from-susan";
    private const string PagePath = "people/susan.md";

    private readonly TemporaryDirectory _remoteRoot = new();
    private readonly string _remote;
    private readonly TestWiki _wiki;

    public WikiBackupTests()
    {
        _remote = _remoteRoot.File("wiki-backup.git");
        _wiki = new TestWiki(new Dictionary<string, string?>
        {
            [$"{WikiBackupSettings.SectionName}:{nameof(WikiBackupSettings.Remote)}"] = _remote
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _wiki.DisposeAsync();
        _remoteRoot.Dispose();
    }

    [Test]
    public async Task TheAppStartsWithABackupRemote()
    {
        await GitAsync(_remoteRoot.Path, "init", "--quiet", "--bare", _remote);

        await _wiki.InitialiseAsync();
        string backedUp = await GitAsync(_remote, "log", "--format=%s", "main");
        HealthReportEntry backup = await CheckBackupAsync();

        ItShouldPushTheInitialCommit();
        ItShouldReportTheBackupAsHealthy();

        void ItShouldPushTheInitialCommit() => backedUp.ShouldBe("Initial wiki");

        void ItShouldReportTheBackupAsHealthy()
        {
            backup.Status.ShouldBe(HealthStatus.Healthy);
            backup.Description.ShouldNotBeNull().ShouldContain("Every commit is pushed.", Case.Sensitive);
        }
    }

    [Test]
    public async Task TheRemoteIsUnreachableUntilAfterTheAppStarted()
    {
        await _wiki.InitialiseAsync();
        HealthReportEntry backupWhileUnreachable = await CheckBackupAsync();
        await GitAsync(_remoteRoot.Path, "init", "--quiet", "--bare", _remote);
        _wiki.FactFinderWritesFacts(Collection);
        _wiki.ThreadWeaver.Behaviour = async (_, cancellationToken) =>
        {
            await _wiki.CallToolAsync(
                nameof(FileTools.CreateFile),
                PagePath,
                () => _wiki.Get<FileTools>().CreateFile(PagePath, "# Susan\n\nAsked for clearer trade-offs.\n", cancellationToken: cancellationToken));
            return "Filed Susan's feedback on the design review.";
        };
        _wiki.Start();

        await CaptureAsync();
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.ProcessedNotesDirectory).Count == 1 && await _wiki.RunsFinishedAsync(2));
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        string backedUp = await GitAsync(_remote, "log", "--format=%s", "main");
        HealthReportEntry backupAfterCatchingUp = await CheckBackupAsync();

        ItShouldFinish();
        ItShouldReportTheFailedPushAsDegraded();
        ItShouldPushEveryCommitWithTheNextOne();
        ItShouldReportTheBackupAsHealthyAgain();

        void ItShouldFinish() => settled.ShouldBeTrue(pipeline);

        void ItShouldReportTheFailedPushAsDegraded()
        {
            backupWhileUnreachable.Status.ShouldBe(HealthStatus.Degraded);
            backupWhileUnreachable.Description.ShouldNotBeNull().ShouldContain("Last push failed at ", Case.Sensitive);
            backupWhileUnreachable.Description.ShouldNotBeNull().ShouldContain("1 commit is not pushed yet.", Case.Sensitive);
        }

        void ItShouldPushEveryCommitWithTheNextOne() => backedUp.Split('\n').ShouldBe(
        [
            "ThreadWeaver: Filed Susan's feedback on the design review.",
            "Initial wiki"
        ]);

        void ItShouldReportTheBackupAsHealthyAgain()
        {
            backupAfterCatchingUp.Status.ShouldBe(HealthStatus.Healthy);
            backupAfterCatchingUp.Description.ShouldNotBeNull().ShouldContain("Every commit is pushed.", Case.Sensitive);
        }
    }

    private async Task<HealthReportEntry> CheckBackupAsync()
    {
        HealthReport report = await _wiki.Get<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == "Backup", CancellationToken.None);
        return report.Entries["Backup"];
    }

    private async Task CaptureAsync()
    {
        Result<string, Error> captured = await _wiki.Get<CaptureWikiNoteCommandHandler>()
            .Handle(new CaptureWikiNoteCommand(Path.GetFileNameWithoutExtension(NoteName), "Susan asked for clearer trade-offs."), CancellationToken.None);
        captured.IsSuccess.ShouldBeTrue();
    }

    private static async Task<string> GitAsync(string directory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git") { RedirectStandardOutput = true, UseShellExecute = false };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(directory);
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
