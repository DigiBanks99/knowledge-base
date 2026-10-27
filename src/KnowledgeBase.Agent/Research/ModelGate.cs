using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research;

/// <summary>
/// The one in-process gate every research agent run passes through, so research never asks the model server for more
/// than <see cref="ResearchSettings.ModelConcurrency"/> runs at once (llama-server is typically started with a single
/// slot, and a second request would only queue there with a timeout running).
/// </summary>
internal sealed class ModelGate(IOptions<ResearchSettings> settings) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(Math.Max(1, settings.Value.ModelConcurrency));

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
        try
        {
            return await work(cancellationToken);
        }
        finally
        {
            _slots.Release();
        }
    }

    public void Dispose() => _slots.Dispose();
}
