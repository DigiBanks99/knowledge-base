using KnowledgeBase.Agent.Research.Triage;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Queries;

/// <summary>How well the Keeper and the research notes did over the recent window.</summary>
public record struct GetResearchQualityQuery;

internal sealed class GetResearchQualityQueryHandler(
    IResearchStore store,
    IOptions<ResearchSettings> settings,
    TimeProvider timeProvider)
{
    public async Task<Result<ResearchQuality, Error>> Handle(GetResearchQualityQuery _, CancellationToken cancellationToken)
    {
        DateTimeOffset since = timeProvider.GetUtcNow() - KeeperAgreement.Window;
        return await store
            .ListRecentBatchesAsync(since, cancellationToken)
            .Map(batches => KeeperAgreement.From(batches, since, settings.Value.KeeperModel));
    }
}
