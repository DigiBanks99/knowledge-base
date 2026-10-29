using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>Records the people, public figures and organisations in facts extracted from a note on the deny-list.</summary>
public record RecordFactSubjectsCommand(IReadOnlyList<Fact> Facts, FactOrigin Origin);

internal sealed class RecordFactSubjectsCommandHandler(
    ILogger<RecordFactSubjectsCommandHandler> logger,
    IResearchStore store)
{
    private static readonly SubjectKind[] ListedKinds = [SubjectKind.Person, SubjectKind.PublicFigure, SubjectKind.Organisation];

    /// <summary>
    /// Only the author's own notes name people and organisations worth keeping private. Research notes name public
    /// companies and authors of articles; denying those would silently degrade every query about them. Public figures
    /// are listed as allowed rather than left out, so the author sees the decision and a later note can still deny
    /// them. Returns how many people, public figures and organisations the facts name.
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
                .Where(subject => subject.Kind is { } kind && ListedKinds.Contains(kind))
        ];
        if (subjects.Length == 0)
        {
            return 0;
        }

        return await store
            .UpdateDenyListAsync(
                denyList => subjects.Aggregate(
                    denyList,
                    (list, subject) => list.WithAutomatic(subject.Name, subject.Kind!.Value, StateOf(subject))),
                cancellationToken)
            .Map(_ => subjects.Length)
            .TapError(logger.FailedToRecordFactSubjects);
    }

    /// <summary>A public figure is allowed only when the model was sure; a doubt leaves the decision to the author.</summary>
    private static DenyListState StateOf(FactSubject subject) => subject switch
    {
        { Kind: SubjectKind.Person or SubjectKind.PublicFigure, PossiblyPublicFigure: true } => DenyListState.Pending,
        { Kind: SubjectKind.PublicFigure } => DenyListState.Allowed,
        _ => DenyListState.Denied
    };
}
