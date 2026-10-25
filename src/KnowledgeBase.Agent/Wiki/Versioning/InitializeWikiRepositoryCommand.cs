using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki.Versioning;

public record struct InitializeWikiRepositoryCommand();

/// <summary>
/// Makes the wiki directory a git repository when it is not one yet, committing the pages already there so the first
/// agent commit only holds that run's changes. Succeeds with <c>false</c> when the repository already existed.
/// </summary>
/// <remarks>
/// The repository's settings are applied on every start, not only when it is created: an earlier start that stopped
/// halfway, or a repository restored from elsewhere, would otherwise leave commits without a committer identity.
/// </remarks>
internal sealed class InitializeWikiRepositoryCommandHandler(
    ILogger<InitializeWikiRepositoryCommandHandler> logger,
    IGitClient git,
    IOptions<WikiConfigurationSettings> settings)
{
    private readonly string _wikiDirectory = settings.Value.WikiDirectory;

    public async Task<Result<bool, Error>> Handle(
        InitializeWikiRepositoryCommand _,
        CancellationToken cancellationToken)
    {
        bool create = !git.HasRepository(_wikiDirectory);
        if (create)
        {
            logger.InitializingWikiRepository(_wikiDirectory);
        }

        Task<Result<bool, Error>> repository = create
            ? git.InitAsync(_wikiDirectory, cancellationToken)
            : Task.FromResult(Result.Success<bool, Error>(false));

        // Line endings are stored as written, so a local run on Windows never rewrites pages.
        return await repository
            .Bind(_ => git.SetConfigAsync(_wikiDirectory, "core.autocrlf", "false", cancellationToken))
            .Bind(_ => git.SetConfigAsync(_wikiDirectory, "user.name", WikiCommitMessage.CommitterName, cancellationToken))
            .Bind(_ => git.SetConfigAsync(_wikiDirectory, "user.email", WikiCommitMessage.Email, cancellationToken))
            .Bind(_ => create ? CommitExistingPages(cancellationToken) : Task.FromResult(Result.Success<bool, Error>(false)))
            .Map(_ => create);
    }

    private async Task<Result<bool, Error>> CommitExistingPages(CancellationToken cancellationToken) =>
        await git.StageAllAsync(_wikiDirectory, cancellationToken)
            .Bind(_ => git.HasStagedChangesAsync(_wikiDirectory, cancellationToken))
            .Bind(hasChanges => hasChanges
                ? git.CommitAsync(_wikiDirectory, WikiCommitMessage.InitialCommit, WikiCommitMessage.DefaultAuthor, cancellationToken)
                : Task.FromResult(Result.Success<bool, Error>(false)));
}