using System.Diagnostics;

namespace KnowledgeBase.Agent.IntegrationTests;

/// <summary>
/// Waits for the file-system driven pipeline to settle. Polling with a generous timeout keeps the tests fast when the
/// pipeline is quick and stable when the machine is slow.
/// </summary>
internal static class Eventually
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Returns whether the condition became true before the timeout.</summary>
    public static Task<bool> Until(Func<bool> condition, TimeSpan? timeout = null) =>
        Until(() => Task.FromResult(condition()), timeout);

    /// <inheritdoc cref="Until(Func{bool}, TimeSpan?)"/>
    public static async Task<bool> Until(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        TimeSpan limit = timeout ?? DefaultTimeout;
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < limit)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(PollInterval);
        }

        return await condition();
    }
}
