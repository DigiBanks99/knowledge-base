using KnowledgeBase.Agent.Runs.Models;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki.Versioning;

public record CommitWikiRunCommand(AgentRun Run);

/// <summary>
/// Commits whatever a run changed in the wiki, attributed to the run. Succeeds with <c>false</c> when the run changed
/// nothing, so no empty commits are made.
/// </summary>
internal sealed class CommitWikiRunCommandHandler(
    IGitClient git,
    IOptions<WikiConfigurationSettings> settings)
{
    private readonly string _wikiDirectory = settings.Value.WikiDirectory;

    public async Task<Result<bool, Error>> Handle(CommitWikiRunCommand command, CancellationToken cancellationToken)
    {
        // Without its own repository, git would find the nearest parent one (the project's, in a local run).
        if (!git.HasRepository(_wikiDirectory))
        {
            return new WikiRepositoryMissingError(_wikiDirectory);
        }

        return await git.StageAllAsync(_wikiDirectory, cancellationToken)
            .Bind(_ => git.HasStagedChangesAsync(_wikiDirectory, cancellationToken))
            .Bind(hasChanges => hasChanges
                ? git.CommitAsync(
                    _wikiDirectory,
                    WikiCommitMessage.For(command.Run),
                    WikiCommitMessage.Author(command.Run.Agent),
                    cancellationToken)
                : Task.FromResult(Result.Success<bool, Error>(false)));
    }
}
