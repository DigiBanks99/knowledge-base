using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Fetch;

/// <summary>
/// Spaces requests to the same host by <see cref="ResearchSettings.FetchPolitenessDelay"/>, so the fetcher never
/// hammers one site. Each caller reserves the next free slot, so concurrent callers queue rather than collide.
/// </summary>
internal sealed class PolitenessGate(IOptions<ResearchSettings> settings, TimeProvider timeProvider)
{
    private const int PruneThreshold = 1000;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _nextFree = new(StringComparer.OrdinalIgnoreCase);

    public async Task WaitAsync(string host, CancellationToken cancellationToken)
    {
        TimeSpan wait = Reserve(host, timeProvider.GetUtcNow());
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, timeProvider, cancellationToken);
        }
    }

    /// <summary>Reserves the next slot for <paramref name="host"/> and returns how long until it starts.</summary>
    public TimeSpan Reserve(string host, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_nextFree.Count > PruneThreshold)
            {
                foreach (string idle in _nextFree.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToList())
                {
                    _nextFree.Remove(idle);
                }
            }

            DateTimeOffset start = _nextFree.TryGetValue(host, out DateTimeOffset nextFree) && nextFree > now ? nextFree : now;
            _nextFree[host] = start + settings.Value.FetchPolitenessDelay;
            return start - now;
        }
    }
}
