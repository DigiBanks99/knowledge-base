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
/// with the async context, so tool-call middleware and tools can find it without it being passed around.
/// </summary>
internal sealed class AgentRunTracker(
    ILogger<AgentRunTracker> logger,
    AgentRunStore store,
    TimeProvider timeProvider)
{
    private static readonly AsyncLocal<RunRecorder?> CurrentRecorder = new();

    /// <summary>The run executing in the current async context, if any.</summary>
    public AgentRun? Current => CurrentRecorder.Value?.Snapshot();

    public async Task<Result<AgentRun, Error>> RunAsync(
        string agent,
        AgentRunTrigger trigger,
        Func<CancellationToken, Task<AgentResponse>> run,
        CancellationToken cancellationToken)
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
            []));
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

        await SaveAsync(recorder);
        AgentRun completed = recorder.Snapshot();
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

    private async Task SaveAsync(RunRecorder recorder)
    {
        try
        {
            await recorder.SaveAsync(store);
        }
        catch (IOException ex)
        {
            // Tracking must never break the pipeline; the next save will catch up.
            logger.FailedToSaveRun(ex);
        }
    }

    private sealed class RunRecorder(AgentRun run)
    {
        private readonly Lock _lock = new();
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private readonly List<AgentToolCall> _toolCalls = [];
        private AgentRun _run = run;

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

        public async Task SaveAsync(AgentRunStore store)
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