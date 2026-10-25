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
}