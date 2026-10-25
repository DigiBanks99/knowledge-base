using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Queries;

public record struct ListDenyListQuery;

internal sealed class ListDenyListQueryHandler(IResearchStore store)
{
    public async Task<Result<IReadOnlyList<DenyListEntry>, Error>> Handle(
        ListDenyListQuery _,
        CancellationToken cancellationToken) => await store
        .ReadDenyListAsync(cancellationToken)
        .Map(IReadOnlyList<DenyListEntry> (denyList) =>
            [.. denyList.Entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)]);
}
