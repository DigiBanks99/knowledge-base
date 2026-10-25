namespace KnowledgeBase.Agent.Wiki.Versioning;

/// <summary>
/// The git commands the wiki's versioning needs, run against a repository directory. Failures (git missing, a
/// non-zero exit or a timeout) come back as a <see cref="GitCommandFailedError"/> rather than an exception.
/// </summary>
internal interface IGitClient
{
    /// <summary>
    /// Whether the directory is the root of its own repository. A directory nested in another repository is not, so
    /// commands never reach a parent repository by accident.
    /// </summary>
    bool HasRepository(string directory);

    Task<Result<bool, Error>> InitAsync(string directory, CancellationToken cancellationToken);

    Task<Result<bool, Error>> SetConfigAsync(string directory, string key, string value, CancellationToken cancellationToken);

    /// <summary>Stages every change in the working tree, including new and deleted files.</summary>
    Task<Result<bool, Error>> StageAllAsync(string directory, CancellationToken cancellationToken);

    Task<Result<bool, Error>> HasStagedChangesAsync(string directory, CancellationToken cancellationToken);

    /// <summary>Commits the staged changes. <paramref name="author"/> is in git's <c>Name &lt;email&gt;</c> form.</summary>
    Task<Result<bool, Error>> CommitAsync(string directory, string message, string author, CancellationToken cancellationToken);

    /// <summary>Points the named remote at <paramref name="url"/>, adding it when it does not exist yet.</summary>
    Task<Result<bool, Error>> SetRemoteAsync(string directory, string name, string url, CancellationToken cancellationToken);

    /// <summary>
    /// Pushes the current branch to the named remote and tracks it there. Every commit not on the remote yet is sent,
    /// so a push after a failed one catches up. <paramref name="sshCommand"/>, when given, replaces the SSH command
    /// git connects with.
    /// </summary>
    Task<Result<bool, Error>> PushAsync(string directory, string remote, string? sshCommand, CancellationToken cancellationToken);

    /// <summary>How many commits on the current branch the named remote does not have, as of the last push.</summary>
    Task<Result<int, Error>> CountUnpushedCommitsAsync(string directory, string remote, CancellationToken cancellationToken);

    /// <summary>The newest commit on the current branch, or <c>null</c> when there are none yet.</summary>
    Task<Result<GitCommit?, Error>> GetLastCommitAsync(string directory, CancellationToken cancellationToken);
}

public sealed record GitCommit(string Sha, DateTimeOffset CommittedAt, string Subject);