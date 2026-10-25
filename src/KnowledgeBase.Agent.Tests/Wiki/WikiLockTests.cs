using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Tests.Wiki;

public sealed class WikiLockTests : IDisposable
{
    private readonly WikiLock _lock = new();

    public void Dispose() => _lock.Dispose();

    [Test]
    public async Task TheWikiIsLocked()
    {
        using IDisposable held = await _lock.AcquireAsync(CancellationToken.None);

        Task<IDisposable> next = _lock.AcquireAsync(CancellationToken.None);

        ItShouldMakeTheNextAgentWait();

        void ItShouldMakeTheNextAgentWait() => next.IsCompleted.ShouldBeFalse();
    }

    [Test]
    public async Task TheWikiIsReleased()
    {
        IDisposable held = await _lock.AcquireAsync(CancellationToken.None);
        Task<IDisposable> next = _lock.AcquireAsync(CancellationToken.None);

        held.Dispose();

        await ItShouldLetTheNextAgentIn();

        async Task ItShouldLetTheNextAgentIn() =>
            (await next.WaitAsync(TimeSpan.FromSeconds(5))).ShouldNotBeNull();
    }

    [Test]
    public async Task TheWikiIsReleasedTwice()
    {
        IDisposable held = await _lock.AcquireAsync(CancellationToken.None);

        held.Dispose();
        held.Dispose();
        using IDisposable first = await _lock.AcquireAsync(CancellationToken.None);
        Task<IDisposable> second = _lock.AcquireAsync(CancellationToken.None);

        ItShouldOnlyReleaseOnce();

        void ItShouldOnlyReleaseOnce() => second.IsCompleted.ShouldBeFalse();
    }

    [Test]
    public async Task AWaitingAgentIsCancelled()
    {
        using IDisposable held = await _lock.AcquireAsync(CancellationToken.None);

        Task<IDisposable> next = _lock.AcquireAsync(new CancellationToken(canceled: true));

        await ItShouldStopWaiting();

        async Task ItShouldStopWaiting() => await Should.ThrowAsync<OperationCanceledException>(next);
    }
}
