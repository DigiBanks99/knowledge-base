using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research;

/// <summary>An <see cref="IResearchStore"/> substitute that holds the deny-list in memory, applying updates as the real store does.</summary>
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
}
