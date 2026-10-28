namespace KnowledgeBase.Agent.Research;

/// <summary>When the search job next runs, kept for the Status page; none until the job has scheduled a cycle.</summary>
internal sealed class ResearchCycleClock
{
    private long _nextCycleTicks;

    public DateTimeOffset? NextCycleAt
    {
        get
        {
            long ticks = Interlocked.Read(ref _nextCycleTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public void NextCycle(DateTimeOffset at) => Interlocked.Exchange(ref _nextCycleTicks, at.UtcTicks);
}
