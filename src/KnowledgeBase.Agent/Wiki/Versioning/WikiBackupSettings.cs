namespace KnowledgeBase.Agent.Wiki.Versioning;

/// <summary>
/// The optional private repository the wiki's history is pushed to. Backup is off unless <see cref="Remote"/> is set.
/// </summary>
public class WikiBackupSettings
{
    public const string SectionName = "Wiki:Backup";
    public const string RemoteName = "backup";
    public const string KeyFileName = "id_ed25519";
    public const string KnownHostsFileName = "known_hosts";

    /// <summary>The backup repository's SSH URL, such as <c>git@github.com:owner/wiki-backup.git</c>.</summary>
    public string? Remote { get; init; }

    /// <summary>
    /// The folder holding the deploy key (<c>id_ed25519</c>) and the remote's pinned host keys (<c>known_hosts</c>).
    /// Without it, git uses the machine's own SSH setup, which is enough for a local run.
    /// </summary>
    public string? KeyDirectory { get; init; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Remote);

    /// <summary>
    /// The SSH command git pushes with: only the deploy key is offered, and the remote must match a pinned host key,
    /// so the push can neither fall back to another identity nor trust a host it has not seen before.
    /// </summary>
    public string? SshCommand()
    {
        if (string.IsNullOrWhiteSpace(KeyDirectory))
        {
            return null;
        }

        string key = Quote(Path.Combine(KeyDirectory, KeyFileName));
        string knownHosts = Quote(Path.Combine(KeyDirectory, KnownHostsFileName));
        return $"ssh -i {key} -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes -o UserKnownHostsFile={knownHosts} -o BatchMode=yes";
    }

    // git runs the command through a shell, so paths are single-quoted, with any single quote closed and escaped.
    private static string Quote(string path) => $"'{path.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal)}'";
}