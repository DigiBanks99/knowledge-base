namespace KnowledgeBase.Agent.IntegrationTests;

/// <summary>
/// The real clock, with every timer running <paramref name="factor"/> times faster. The pipeline's debounces, polls
/// and retries keep their order and proportions but take a fraction of the time, so tests stay quick without a fake
/// clock that every test would have to advance by hand.
/// </summary>
internal sealed class AcceleratedTimeProvider(int factor) : TimeProvider
{
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        new AcceleratedTimer(this, TimeProvider.System.CreateTimer(callback, state, Scale(dueTime), Scale(period)));

    private TimeSpan Scale(TimeSpan interval) =>
        interval <= TimeSpan.Zero || interval == Timeout.InfiniteTimeSpan ? interval : interval / factor;

    private sealed class AcceleratedTimer(AcceleratedTimeProvider provider, ITimer timer) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) =>
            timer.Change(provider.Scale(dueTime), provider.Scale(period));

        public void Dispose() => timer.Dispose();

        public ValueTask DisposeAsync() => timer.DisposeAsync();
    }
}