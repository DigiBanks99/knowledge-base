using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;

namespace KnowledgeBase.Agent.Research.Search;

/// <summary>
/// Keeps the runs that record topic searches. Recording is a record of research, not part of it: neither method ever
/// fails or throws, so a run store that cannot be written never stops a search, a fetch or a triage.
/// </summary>
internal interface ISearchRunLog
{
    /// <summary>
    /// Saves a search's run, first as running and again when it ends. Lines already added to a saved copy (a page
    /// fetched in between) are kept after the run's own.
    /// </summary>
    Task RecordAsync(AgentRun run, CancellationToken cancellationToken);

    /// <summary>
    /// Adds what became of a search's results to its run. A line already recorded for the same step and target is
    /// skipped, so a step retried after a restart does not repeat itself. A run that no longer exists is left alone.
    /// </summary>
    Task AppendAsync(string runId, IReadOnlyList<AgentToolCall> lines, CancellationToken cancellationToken);
}

internal sealed class SearchRunLog(ILogger<SearchRunLog> logger, IAgentRunStore store) : ISearchRunLog, IDisposable
{
    // The search, fetching and triage can all write a run; reading and writing it back is serialised so none loses
    // another's lines.
    private readonly SemaphoreSlim _lock = new(1, 1);

    public void Dispose() => _lock.Dispose();

    public async Task RecordAsync(AgentRun run, CancellationToken cancellationToken)
    {
        try
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                AgentRun? saved = await store.GetAsync(run.Id, cancellationToken);
                await store.SaveAsync(saved is null ? run : run with { ToolCalls = Merge(run.ToolCalls, saved.ToolCalls) }, cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.FailedToRecordSearch(run.Topic ?? string.Empty, ex);
        }
    }

    public async Task AppendAsync(string runId, IReadOnlyList<AgentToolCall> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return;
        }

        try
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (await store.GetAsync(runId, cancellationToken) is not { } run)
                {
                    return;
                }

                IReadOnlyList<AgentToolCall> merged = Merge(run.ToolCalls, lines);
                if (merged.Count > run.ToolCalls.Count)
                {
                    await store.SaveAsync(run with { ToolCalls = merged }, cancellationToken);
                }
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.FailedToAppendToSearchRun(runId, ex);
        }
    }

    /// <summary><paramref name="first"/>, then each line of <paramref name="then"/> for a step and target not yet in it.</summary>
    private static IReadOnlyList<AgentToolCall> Merge(IReadOnlyList<AgentToolCall> first, IReadOnlyList<AgentToolCall> then) =>
    [
        .. first,
        .. then.Where(line => !first.Any(call => call.Tool == line.Tool && call.Target == line.Target))
    ];
}
