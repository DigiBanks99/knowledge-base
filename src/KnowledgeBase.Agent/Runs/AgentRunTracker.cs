using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Runs.Models;

using Microsoft.Agents.AI;

namespace KnowledgeBase.Agent.Runs;

public sealed class AgentRunFailedError(AgentRun run)
    : Error("AgentRunFailed", $"{run.Agent} run '{run.Id}' failed: {run.Error}")
{
    public AgentRun Run { get; } = run;
}

/// <summary>
/// Records pipeline agent runs: their trigger, status, outcome and every tool call. The run being executed flows
/// with the async context, so tool-call middleware and tools can find it without it being passed around. When a run
/// ends, every registered <see cref="IAgentRunObserver"/> is told about it.
/// </summary>
internal sealed class AgentRunTracker(
    ILogger<AgentRunTracker> logger,
    IAgentRunStore store,
    IEnumerable<IAgentRunObserver> observers,
    TimeProvider timeProvider)
{
    private static readonly AsyncLocal<RunRecorder?> CurrentRecorder = new();

    private static readonly TimeSpan[] CompletedSaveRetryDelays =
        [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)];

    /// <summary>The run executing in the current async context, if any.</summary>
    public AgentRun? Current => CurrentRecorder.Value?.Snapshot();

    /// <summary>
    /// The origin of the run executing in the current async context, or <see cref="FactOrigin.Author"/> outside a run.
    /// Cheaper than <see cref="Current"/>, which copies the run's tool calls.
    /// </summary>
    public FactOrigin CurrentOrigin => CurrentRecorder.Value?.Origin ?? FactOrigin.Author;

    public Task<Result<AgentRun, Error>> RunAsync(
        string agent,
        AgentRunTrigger trigger,
        Func<CancellationToken, Task<AgentResponse>> run,
        CancellationToken cancellationToken) =>
        RunAsync(agent, trigger, FactOrigin.Author, null, run, cancellationToken);

    /// <summary>
    /// Runs an agent over content of the given origin and, for research, the topic it was gathered for. The origin
    /// flows with the run, so tools can refuse what a run over research may not do.
    /// </summary>
    public async Task<Result<AgentRun, Error>> RunAsync(
        string agent,
        AgentRunTrigger trigger,
        FactOrigin origin,
        string? topic,
        Func<CancellationToken, Task<AgentResponse>> run,
        CancellationToken cancellationToken,
        string? sourceNote = null)
    {
        RunRecorder recorder = new(new AgentRun(
            $"{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}-{agent.ToLowerInvariant()}-{Guid.NewGuid():N}"[..40],
            agent,
            trigger,
            AgentRunStatus.Running,
            timeProvider.GetUtcNow(),
            null,
            null,
            null,
            [],
            origin,
            topic,
            sourceNote));
        CurrentRecorder.Value = recorder;
        await SaveAsync(recorder);

        try
        {
            AgentResponse response = await run(cancellationToken);
            recorder.Complete(AgentRunStatus.Succeeded, timeProvider.GetUtcNow(), response.Text, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.AgentRunFailed(agent, ex);
            recorder.Complete(AgentRunStatus.Failed, timeProvider.GetUtcNow(), null, ex.Message);
        }
        finally
        {
            CurrentRecorder.Value = null;
        }

        await SaveCompletedAsync(recorder);
        AgentRun completed = recorder.Snapshot();
        await NotifyObserversAsync(completed, cancellationToken);
        return completed.Status == AgentRunStatus.Succeeded
            ? completed
            : new AgentRunFailedError(completed);
    }

    public async Task RecordToolCallAsync(string tool, string? target, string? error)
    {
        RunRecorder? recorder = CurrentRecorder.Value;
        if (recorder is null)
        {
            return;
        }

        recorder.Add(new AgentToolCall(tool, target, timeProvider.GetUtcNow(), error is null, error));
        await SaveAsync(recorder);
    }

    private async Task NotifyObserversAsync(AgentRun run, CancellationToken cancellationToken)
    {
        foreach (IAgentRunObserver observer in observers)
        {
            try
            {
                await observer.OnRunCompletedAsync(run, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // An observer reacts to a run; it must never change the run's outcome or silence the others.
                logger.RunObserverFailed(observer.GetType().Name, run.Agent, run.Id, ex);
            }
        }
    }

    private async Task SaveAsync(RunRecorder recorder)
    {
        try
        {
            await recorder.SaveAsync(store);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tracking must never break the pipeline; the next save will catch up.
            logger.FailedToSaveRun(ex);
        }
    }

    /// <summary>
    /// Saves a completed run, retrying briefly: no later save would catch up, so a lost write would leave the run
    /// marked as running until a restart fails it.
    /// </summary>
    private async Task SaveCompletedAsync(RunRecorder recorder)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await recorder.SaveAsync(store);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < CompletedSaveRetryDelays.Length)
            {
                await Task.Delay(CompletedSaveRetryDelays[attempt], timeProvider);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AgentRun run = recorder.Snapshot();
                logger.FailedToSaveCompletedRun(run.Agent, run.Id, ex);
                return;
            }
        }
    }

    private sealed class RunRecorder(AgentRun run)
    {
        private readonly Lock _lock = new();
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private readonly List<AgentToolCall> _toolCalls = [];
        private AgentRun _run = run;

        public FactOrigin Origin { get; } = run.Origin;

        public AgentRun Snapshot()
        {
            lock (_lock)
            {
                return _run with { ToolCalls = [.. _toolCalls] };
            }
        }

        public void Add(AgentToolCall toolCall)
        {
            lock (_lock)
            {
                _toolCalls.Add(toolCall);
            }
        }

        public void Complete(AgentRunStatus status, DateTimeOffset completedAt, string? summary, string? error)
        {
            lock (_lock)
            {
                _run = _run with { Status = status, CompletedAt = completedAt, Summary = summary, Error = error };
            }
        }

        public async Task SaveAsync(IAgentRunStore store)
        {
            await _saveGate.WaitAsync();
            try
            {
                await store.SaveAsync(Snapshot(), CancellationToken.None);
            }
            finally
            {
                _saveGate.Release();
            }
        }
    }
}
