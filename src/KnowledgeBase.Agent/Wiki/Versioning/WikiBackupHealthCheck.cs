using System.Globalization;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki.Versioning;

/// <summary>
/// Whether the wiki's history reaches the backup remote: the last commit, the last successful push and, when the last
/// push failed, why. Backup being off is not a problem, so it is reported as healthy.
/// </summary>
internal sealed class WikiBackupHealthCheck(
    IGitClient git,
    WikiBackupState state,
    IOptions<WikiConfigurationSettings> wikiSettings,
    IOptions<WikiBackupSettings> backupSettings)
    : IHealthCheck
{
    private readonly string _wikiDirectory = wikiSettings.Value.WikiDirectory;
    private readonly WikiBackupSettings _backup = backupSettings.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_backup.IsEnabled)
        {
            return HealthCheckResult.Healthy("Off; no backup remote is configured.");
        }

        WikiBackupOutcome outcome = state.Outcome;
        string lastSuccess = $"Last successful push: {(outcome.LastSuccessAt is { } at ? Format(at) : "none since the app started")}.";
        string repository = git.HasRepository(_wikiDirectory)
            ? await DescribeRepositoryAsync(cancellationToken)
            : "The wiki is not a git repository.";

        return outcome.LastPushFailed
            ? new HealthCheckResult(
                context.Registration.FailureStatus,
                $"Last push failed at {Format(outcome.LastAttemptAt!.Value)}: {outcome.LastError} {lastSuccess} {repository}")
            : HealthCheckResult.Healthy($"{lastSuccess} {repository}");
    }

    private async Task<string> DescribeRepositoryAsync(CancellationToken cancellationToken)
    {
        Result<GitCommit?, Error> lastCommit = await git.GetLastCommitAsync(_wikiDirectory, cancellationToken);
        Result<int, Error> unpushed = await git.CountUnpushedCommitsAsync(
            _wikiDirectory,
            WikiBackupSettings.RemoteName,
            cancellationToken);

        string commit = lastCommit.IsSuccess
            ? lastCommit.Value is { } last
                ? $"Last commit: {last.Sha} \"{last.Subject}\" at {Format(last.CommittedAt)}."
                : "No commits yet."
            : $"The last commit could not be read: {lastCommit.Error.Message}";
        string waiting = unpushed.IsSuccess
            ? unpushed.Value switch
            {
                0 => "Every commit is pushed.",
                1 => "1 commit is not pushed yet.",
                int count => $"{count} commits are not pushed yet."
            }
            : $"Unpushed commits could not be counted: {unpushed.Error.Message}";
        return $"{commit} {waiting}";
    }

    private static string Format(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
