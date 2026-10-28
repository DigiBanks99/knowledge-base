using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Research.Search;

/// <summary>
/// Whether a search source answers, as a status signal. The status publisher runs every signal every 30 seconds, which
/// would be thousands of calls a day to a public API, so a probe's answer is reused for <see cref="ProbeInterval"/>.
/// Registered as a singleton so the reused answer survives between runs.
/// </summary>
internal sealed class SearchSourceHealthCheck<TSource>(TSource source, TimeProvider timeProvider) : IHealthCheck, IDisposable
    where TSource : ISearchSource
{
    public static TimeSpan ProbeInterval { get; } = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _probeLock = new(1, 1);
    private (UnitResult<Error> Answer, DateTimeOffset At)? _lastProbe;

    public void Dispose() => _probeLock.Dispose();

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!source.IsConfigured)
        {
            return HealthCheckResult.Healthy($"Not used; {source.Name} is not configured.");
        }

        UnitResult<Error> answer = await ProbeAsync(cancellationToken);
        return answer.IsSuccess
            ? HealthCheckResult.Healthy("Reachable.")
            : new HealthCheckResult(context.Registration.FailureStatus, answer.Error.Message);
    }

    private async Task<UnitResult<Error>> ProbeAsync(CancellationToken cancellationToken)
    {
        await _probeLock.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (_lastProbe is { } last && now - last.At < ProbeInterval)
            {
                return last.Answer;
            }

            UnitResult<Error> answer = await source.ProbeAsync(cancellationToken);
            _lastProbe = (answer, now);
            return answer;
        }
        finally
        {
            _probeLock.Release();
        }
    }
}
