using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research;

/// <summary>Who gets the model first when several research runs wait for it.</summary>
internal enum ModelPriority
{
    /// <summary>The Pleb: curation can wait for triage, which holds up a batch of fetched results.</summary>
    Normal,

    /// <summary>The Keeper, which shares the CPU with the chat model.</summary>
    High
}

/// <summary>
/// The one in-process gate every research agent run passes through, so research never asks the model server for more
/// than <see cref="ResearchSettings.ModelConcurrency"/> runs at once (llama-server is typically started with a single
/// slot, and a second request would only queue there with a timeout running). When a slot frees, a waiting
/// <see cref="ModelPriority.High"/> run goes before any <see cref="ModelPriority.Normal"/> one, and runs of the same
/// priority go in the order they asked.
/// </summary>
internal sealed class ModelGate(IOptions<ResearchSettings> settings) : IDisposable
{
    private readonly Lock _lock = new();
    private readonly Queue<TaskCompletionSource> _high = new();
    private readonly Queue<TaskCompletionSource> _normal = new();
    private int _free = Math.Max(1, settings.Value.ModelConcurrency);

    public async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken,
        ModelPriority priority = ModelPriority.Normal)
    {
        await AcquireAsync(priority, cancellationToken);
        try
        {
            return await work(cancellationToken);
        }
        finally
        {
            Release();
        }
    }

    public void Dispose()
    {
    }

    private Task AcquireAsync(ModelPriority priority, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_free > 0)
            {
                _free--;
                return Task.CompletedTask;
            }

            (priority == ModelPriority.High ? _high : _normal).Enqueue(waiter);
        }

        // A waiter that gives up stays queued as a cancelled task, which Release skips, so nothing is searched for.
        CancellationTokenRegistration registration = cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
        _ = waiter.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        return waiter.Task;
    }

    private void Release()
    {
        lock (_lock)
        {
            while (_high.TryDequeue(out TaskCompletionSource? next) || _normal.TryDequeue(out next))
            {
                // The slot passes on only to a waiter that is still waiting.
                if (next.TrySetResult())
                {
                    return;
                }
            }

            _free++;
        }
    }
}
