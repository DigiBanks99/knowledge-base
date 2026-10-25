using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki.Versioning;

/// <param name="OnlyWhenBehind">
/// Push only when the remote is missing commits, so a start with everything backed up makes no network call.
/// </param>
public record struct PushWikiCommand(bool OnlyWhenBehind = false);

/// <summary>
/// Pushes the wiki's history to the backup remote, logging and recording how it went for the Status page. Succeeds
/// with <c>false</c> when backup is off or nothing needed pushing. A failed push needs no retry queue: the next push
/// sends every commit the remote is missing.
/// </summary>
/// <remarks>
/// The remote is pointed at the configured URL before each push, so a changed URL takes effect without a migration,
/// and a URL git rejects shows on the Status page like any other failed push.
/// </remarks>
internal sealed class PushWikiCommandHandler(
    ILogger<PushWikiCommandHandler> logger,
    IGitClient git,
    WikiBackupState state,
    IOptions<WikiConfigurationSettings> wikiSettings,
    IOptions<WikiBackupSettings> backupSettings)
{
    private readonly string _wikiDirectory = wikiSettings.Value.WikiDirectory;
    private readonly WikiBackupSettings _backup = backupSettings.Value;

    public async Task<Result<bool, Error>> Handle(PushWikiCommand command, CancellationToken cancellationToken)
    {
        if (!_backup.IsEnabled)
        {
            return false;
        }

        return await git
            .SetRemoteAsync(_wikiDirectory, WikiBackupSettings.RemoteName, _backup.Remote!.Trim(), cancellationToken)
            .Bind(_ => command.OnlyWhenBehind
                ? git.CountUnpushedCommitsAsync(_wikiDirectory, WikiBackupSettings.RemoteName, cancellationToken)
                    .Map(unpushed => unpushed > 0)
                : Task.FromResult(Result.Success<bool, Error>(true)))
            .Bind(push => push ? PushAsync(cancellationToken) : Task.FromResult(Result.Success<bool, Error>(false)))
            .TapError(error =>
            {
                state.RecordFailure(error);
                logger.FailedToPushWiki(error);
            });
    }

    private async Task<Result<bool, Error>> PushAsync(CancellationToken cancellationToken) => await git
        .PushAsync(_wikiDirectory, WikiBackupSettings.RemoteName, _backup.SshCommand(), cancellationToken)
        .Tap(() =>
        {
            state.RecordSuccess();
            logger.PushedWiki();
        });
}
