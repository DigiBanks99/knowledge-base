using System.ComponentModel;

using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>
/// The author's decision about a name before or regardless of classification, for example allowing a public figure
/// before any note names them.
/// </summary>
public record AddDenyListEntryCommand(
    [property: Description("The person's or organisation's name.")]
    string Name,
    [property: Description("`Person`, `PublicFigure` or `Organisation`.")]
    SubjectKind Kind,
    [property: Description("`Denied` or `Allowed`.")]
    DenyListState State);

internal sealed class AddDenyListEntryCommandHandler(
    ILogger<AddDenyListEntryCommandHandler> logger,
    IResearchStore store)
{
    public async Task<Result<DenyListEntry, Error>> Handle(
        AddDenyListEntryCommand request,
        CancellationToken cancellationToken) => await Validate(request)
        .Bind(_ => store.UpdateDenyListAsync(
            denyList => denyList.WithManualEntry(request.Name, request.Kind, request.State),
            cancellationToken))
        .Map(denyList => denyList.Find(request.Name)!)
        .Tap(entry => logger.DenyListEntrySet(entry.Name, entry.State));

    private static Result<AddDenyListEntryCommand, Error> Validate(AddDenyListEntryCommand request) => Result
        .Success<AddDenyListEntryCommand, Error>(request)
        // A name of only separators has no words to match, so it would be listed but deny nothing.
        .Ensure(
            command => DenyListMatcher.Words(command.Name ?? string.Empty).Length > 0,
            new ValidationError("DenyListNameRequired", "The name of the deny-list entry is required."))
        .Ensure(
            command => command.Kind is SubjectKind.Person or SubjectKind.PublicFigure or SubjectKind.Organisation,
            new ValidationError("DenyListKindInvalid", "A deny-list entry is a person, a public figure or an organisation."))
        .Ensure(
            command => command.State is DenyListState.Denied or DenyListState.Allowed,
            new ValidationError("DenyListStateInvalid", "A name can only be denied or allowed."));
}
