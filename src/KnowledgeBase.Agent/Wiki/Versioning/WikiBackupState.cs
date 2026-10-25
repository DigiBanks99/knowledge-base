namespace KnowledgeBase.Agent.Wiki.Versioning;

/// <summary>How the last push to the backup remote went.</summary>
/// <param name="LastAttemptAt">When a push was last tried, or <c>null</c> when none has been since the app started.</param>
/// <param name="LastSuccessAt">When a push last succeeded since the app started.</param>
/// <param name="LastError">Why the last push failed, or <c>null</c> when it succeeded.</param>
public sealed record WikiBackupOutcome(DateTimeOffset? LastAttemptAt, DateTimeOffset? LastSuccessAt, string? LastError)
{
    public static readonly WikiBackupOutcome None = new(null, null, null);

    public bool LastPushFailed => LastError is not null;
}

/// <summary>
/// Remembers the outcome of the latest push for the Status page. It is kept in memory only: what still needs pushing
/// after a restart is read from the repository instead.
/// </summary>
internal sealed class WikiBackupState(TimeProvider timeProvider)
{
    private readonly Lock _lock = new();
    private WikiBackupOutcome _outcome = WikiBackupOutcome.None;

    public WikiBackupOutcome Outcome
    {
        get
        {
            lock (_lock)
            {
                return _outcome;
            }
        }
    }

    public void RecordSuccess()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            _outcome = new WikiBackupOutcome(now, now, null);
        }
    }

    public void RecordFailure(Error error)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            _outcome = _outcome with { LastAttemptAt = now, LastError = error.Message };
        }
    }
}
