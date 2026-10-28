using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research;

/// <summary>An <see cref="IResearchStore"/> substitute that holds research state in memory, applying updates as the real store does.</summary>
internal static class FakeResearchStore
{
    public static IResearchStore Holding(DenyList initial, out Func<DenyList> current)
    {
        DenyList denyList = initial;
        current = () => denyList;

        IResearchStore store = Substitute.For<IResearchStore>();
        store.ReadDenyListAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success<DenyList, Error>(denyList));
        store.UpdateDenyListAsync(Arg.Any<Func<DenyList, Result<DenyList, Error>>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Result<DenyList, Error> updated = call.Arg<Func<DenyList, Result<DenyList, Error>>>()(denyList);
                if (updated.IsSuccess)
                {
                    denyList = updated.Value;
                }

                return updated;
            });

        return store;
    }

    public static IResearchStore Holding(ResearchTopics topics)
    {
        IResearchStore store = Substitute.For<IResearchStore>();
        store.ListTopicsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success<IReadOnlyList<ResearchTopic>, Error>([.. topics.Topics.Values]));
        store.UpdateTopicAsync(
                Arg.Any<string>(),
                Arg.Any<Func<Maybe<ResearchTopic>, Result<Maybe<ResearchTopic>, Error>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string topic = call.Arg<string>();
                Result<Maybe<ResearchTopic>, Error> updated = call
                    .Arg<Func<Maybe<ResearchTopic>, Result<Maybe<ResearchTopic>, Error>>>()(Maybe.From(topics.Topics.GetValueOrDefault(topic)));
                if (updated.IsSuccess && updated.Value.HasValue)
                {
                    topics.Topics[topic] = updated.Value.Value;
                }
                else if (updated.IsSuccess)
                {
                    topics.Topics.Remove(topic);
                    topics.CurationRequests.Remove(topic);
                    topics.FailedCurations.Remove(topic);
                    topics.PendingBatches.Remove(topic);
                    topics.AwaitingBatches.Remove(topic);
                }

                return updated;
            });
        store.ListCurationRequestsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success<IReadOnlyList<CurationRequest>, Error>([.. topics.CurationRequests.Values]));
        store.RequestCurationAsync(Arg.Any<CurationRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                CurationRequest request = call.Arg<CurationRequest>();
                CurationRequest merged = topics.CurationRequests.TryGetValue(request.Topic, out CurationRequest? waiting)
                    ? waiting.Merge(request)
                    : request;
                topics.CurationRequests[request.Topic] = merged;
                return Result.Success<CurationRequest, Error>(merged);
            });
        store.CompleteCurationAsync(Arg.Any<CurationRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                CurationRequest curated = call.Arg<CurationRequest>();
                RemoveUnlessChanged(curated);
                topics.FailedCurations.Remove(curated.Topic);
                return UnitResult.Success<Error>();
            });
        store.FailCurationAsync(Arg.Any<FailedCuration>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                FailedCuration failure = call.Arg<FailedCuration>();
                topics.FailedCurations[failure.Request.Topic] = failure;
                RemoveUnlessChanged(failure.Request);
                return UnitResult.Success<Error>();
            });
        store.ListFailedCurationsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success<IReadOnlyList<FailedCuration>, Error>([.. topics.FailedCurations.Values]));
        store.ReadDenyListAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success<DenyList, Error>(topics.DenyList));
        store.ReadTopicAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<Maybe<ResearchTopic>, Error>(Maybe.From(topics.Topics.GetValueOrDefault(call.Arg<string>()))));
        store.ListTopicsInFlightAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success<IReadOnlySet<string>, Error>(topics.PendingBatches.Keys.Concat(topics.AwaitingBatches.Keys).ToHashSet(StringComparer.Ordinal)));
        store.ListPendingBatchesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success<IReadOnlyList<ResearchBatch>, Error>([.. topics.PendingBatches.Values]));
        store.ReadPendingBatchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<Maybe<ResearchBatch>, Error>(Maybe.From(topics.PendingBatches.GetValueOrDefault(call.Arg<string>()))));
        store.WritePendingBatchAsync(Arg.Any<ResearchBatch>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                ResearchBatch batch = call.Arg<ResearchBatch>();
                if (!topics.Topics.ContainsKey(batch.Topic))
                {
                    return Result.Success<Maybe<ResearchBatch>, Error>(Maybe<ResearchBatch>.None);
                }

                topics.PendingBatches[batch.Topic] = batch;
                return Result.Success<Maybe<ResearchBatch>, Error>(Maybe.From(batch));
            });
        store.UpdatePendingBatchAsync(Arg.Any<string>(), Arg.Any<Func<ResearchBatch, ResearchBatch>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string topic = call.Arg<string>();
                if (!topics.PendingBatches.TryGetValue(topic, out ResearchBatch? batch))
                {
                    return Result.Success<Maybe<ResearchBatch>, Error>(Maybe<ResearchBatch>.None);
                }

                ResearchBatch updated = call.Arg<Func<ResearchBatch, ResearchBatch>>()(batch);
                topics.PendingBatches[topic] = updated;
                return Result.Success<Maybe<ResearchBatch>, Error>(Maybe.From(updated));
            });
        store.AwaitBatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string topic = (string)call.Args()[0];
                if (!topics.PendingBatches.Remove(topic, out ResearchBatch? batch))
                {
                    return Result.Success<Maybe<AwaitingBatch>, Error>(Maybe<AwaitingBatch>.None);
                }

                AwaitingBatch awaiting = new(batch, (string)call.Args()[1], (DateTimeOffset)call.Args()[2]);
                topics.AwaitingBatches[topic] = awaiting;
                return Result.Success<Maybe<AwaitingBatch>, Error>(Maybe.From(awaiting));
            });
        store.CompleteBatchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string topic = call.Arg<string>();
                if (!topics.PendingBatches.Remove(topic, out ResearchBatch? batch))
                {
                    return Result.Success<Maybe<ResearchBatch>, Error>(Maybe<ResearchBatch>.None);
                }

                topics.CompletedBatches.Add(batch);
                return Result.Success<Maybe<ResearchBatch>, Error>(Maybe.From(batch));
            });
        store.ReadBlockedDomainsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success<BlockedDomains, Error>(topics.BlockedDomains));

        return store;

        void RemoveUnlessChanged(CurationRequest handled)
        {
            if (topics.CurationRequests.TryGetValue(handled.Topic, out CurationRequest? waiting) && waiting.IsSameAs(handled))
            {
                topics.CurationRequests.Remove(handled.Topic);
            }
        }
    }
}

/// <summary>
/// The topics, curation requests, batches, deny-list and blocked domains a <see cref="FakeResearchStore"/>
/// holds, keyed by topic.
/// </summary>
internal sealed class ResearchTopics
{
    public Dictionary<string, ResearchTopic> Topics { get; } = [];
    public Dictionary<string, CurationRequest> CurationRequests { get; } = [];
    public Dictionary<string, FailedCuration> FailedCurations { get; } = [];
    public DenyList DenyList { get; set; } = DenyList.Empty;
    public Dictionary<string, ResearchBatch> PendingBatches { get; } = [];
    public Dictionary<string, AwaitingBatch> AwaitingBatches { get; } = [];
    public List<ResearchBatch> CompletedBatches { get; } = [];
    public BlockedDomains BlockedDomains { get; set; } = BlockedDomains.None;
}
