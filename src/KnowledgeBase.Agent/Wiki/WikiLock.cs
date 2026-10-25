namespace KnowledgeBase.Agent.Wiki;

/// <summary>
/// Serialises agent runs that read or change the wiki (ThreadWeaver, Reviewer, Editor), so no agent works on pages
/// another agent is halfway through changing.
/// </summary>
internal sealed class WikiLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
