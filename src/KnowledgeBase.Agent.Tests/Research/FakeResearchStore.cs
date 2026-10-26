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

        return store;
    }
}

/// <summary>The topics and curation requests a <see cref="FakeResearchStore"/> holds, keyed by topic.</summary>
internal sealed class ResearchTopics
{
    public Dictionary<string, ResearchTopic> Topics { get; } = [];
    public Dictionary<string, CurationRequest> CurationRequests { get; } = [];
}
