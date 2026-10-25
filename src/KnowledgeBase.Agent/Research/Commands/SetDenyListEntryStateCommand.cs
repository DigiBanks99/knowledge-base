using System.ComponentModel;

using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>The author's decision to deny or allow a name in search queries.</summary>
public record SetDenyListEntryStateCommand(
    [property: Description("The entry's name, as the deny-list spells it (case and word separators do not matter).")]
    string Name,
    DenyListState State);

internal sealed class SetDenyListEntryStateCommandHandler(
    ILogger<SetDenyListEntryStateCommandHandler> logger,
    IResearchStore store)
{
    public async Task<Result<DenyListEntry, Error>> Handle(
        SetDenyListEntryStateCommand request,
        CancellationToken cancellationToken) => await Result
        .SuccessIf<SetDenyListEntryStateCommand, Error>(
            !string.IsNullOrWhiteSpace(request.Name),
            request,
            new ValidationError("DenyListNameRequired", "The name of the deny-list entry is required."))
        .Bind(_ => store.UpdateDenyListAsync(denyList => denyList.WithManual(request.Name, request.State), cancellationToken))
        .Map(denyList => denyList.Find(request.Name)!)
        .Tap(entry => logger.DenyListEntrySet(entry.Name, entry.State));
}