using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Files.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>
/// Denies everyone the wiki already has a <c>people/</c> folder for, once, so people captured before subjects were
/// classified are protected too. Later people are denied as their notes are processed.
/// </summary>
public record struct SeedDenyListFromPeopleCommand;

internal sealed class SeedDenyListFromPeopleCommandHandler(
    ILogger<SeedDenyListFromPeopleCommandHandler> logger,
    IResearchStore store,
    WikiPaths wikiPaths,
    ListFilesHandler listFilesHandler)
{
    private const string PeopleSection = "people";

    public async Task<Result<DenyList, Error>> Handle(
        SeedDenyListFromPeopleCommand _,
        CancellationToken cancellationToken) => await store
        .UpdateDenyListAsync(Seed, cancellationToken)
        .TapError(logger.FailedToSeedDenyList);

    private Result<DenyList, Error> Seed(DenyList denyList)
    {
        if (denyList.SeededFromPeople)
        {
            return denyList;
        }

        Result<string[], Error> people = ListPeopleFolders();
        if (people.IsFailure)
        {
            return people.Error;
        }

        logger.SeedingDenyList(people.Value.Length);
        DenyList seeded = people.Value
            .Aggregate(denyList, (list, folder) => list.WithAutomatic(PersonName(folder), SubjectKind.Person));
        return seeded with { SeededFromPeople = true };
    }

    private Result<string[], Error> ListPeopleFolders() => listFilesHandler
        .Handle(new ListFilesRequest(wikiPaths.Resolve(PeopleSection), IncludeDirectories: true))
        .Map(paths => paths.Where(Directory.Exists).Select(Path.GetFileName).OfType<string>().ToArray())
        .OnFailureCompensate(error => error is DirectoryNotFoundError
            ? Result.Success<string[], Error>([])
            : error);

    /// <summary>A folder named <c>first-name-last-name</c> (as the wiki rules ask) as the name <c>First Name Last Name</c>.</summary>
    internal static string PersonName(string folder) => string.Join(
        ' ',
        DenyListMatcher.Words(folder).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
