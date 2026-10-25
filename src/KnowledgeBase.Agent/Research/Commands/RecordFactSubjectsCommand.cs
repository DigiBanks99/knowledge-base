using KnowledgeBase.Agent.Facts.Models;
namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>Denies the people and organisations in facts extracted from a note.</summary>
public record RecordFactSubjectsCommand(IReadOnlyList<Fact> Facts, FactOrigin Origin);

internal sealed class RecordFactSubjectsCommandHandler(
    ILogger<RecordFactSubjectsCommandHandler> logger,
    IResearchStore store)
{
    private static readonly SubjectKind[] PrivateKinds = [SubjectKind.Person, SubjectKind.Organisation];

    /// <summary>
    /// Only the author's own notes name people and organisations worth keeping private. Research notes name public
    /// companies and authors of articles; denying those would silently degrade every query about them. Returns how
    /// many people and organisations the facts name.
    /// </summary>
    public async Task<Result<int, Error>> Handle(
        RecordFactSubjectsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Origin != FactOrigin.Author)
        {
            return 0;
        }

        FactSubject[] subjects =
        [
            .. request.Facts
                .SelectMany(fact => fact.Observations.SelectMany(observation => observation.RelatedSubjects).Prepend(fact.Subject))
                .Where(subject => subject.Kind is { } kind && PrivateKinds.Contains(kind))
        ];
        if (subjects.Length == 0)
        {
            return 0;
        }

        return await store
            .UpdateDenyListAsync(
                denyList => subjects.Aggregate(denyList, (list, subject) => list.WithAutomatic(subject.Name, subject.Kind!.Value)),
                cancellationToken)
            .Map(_ => subjects.Length)
            .TapError(logger.FailedToRecordFactSubjects);
    }
}
