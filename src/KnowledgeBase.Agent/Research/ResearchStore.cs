using System.Text.Json;

using KnowledgeBase.Agent.Common;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research;

internal sealed class ResearchStore(IOptions<WikiConfigurationSettings> settings) : IResearchStore, IDisposable
{
    private readonly string _denyListPath = settings.Value.DenyListPath;
    private readonly SemaphoreSlim _denyListLock = new(1, 1);

    public void Dispose() => _denyListLock.Dispose();

    // Writes replace the file whole, so a read never sees half of one and needs no lock.
    public Task<Result<DenyList, Error>> ReadDenyListAsync(CancellationToken cancellationToken) =>
        ReadAsync(cancellationToken);

    public async Task<Result<DenyList, Error>> UpdateDenyListAsync(
        Func<DenyList, Result<DenyList, Error>> update,
        CancellationToken cancellationToken)
    {
        await _denyListLock.WaitAsync(cancellationToken);
        try
        {
            Result<DenyList, Error> current = await ReadAsync(cancellationToken);
            if (current.IsFailure)
            {
                return current;
            }

            // An update that changed nothing returns the same instance; rewriting it would only wake the UI.
            Result<DenyList, Error> updated = update(current.Value);
            return updated.IsFailure || ReferenceEquals(updated.Value, current.Value)
                ? updated
                : await WriteAsync(updated.Value, cancellationToken);
        }
        finally
        {
            _denyListLock.Release();
        }
    }

    /// <summary>
    /// A corrupt deny-list is an error rather than an empty list: reading it as empty would let denied names into
    /// queries, and the next change would overwrite every entry.
    /// </summary>
    private async Task<Result<DenyList, Error>> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            DenyList? denyList = await JsonFileStore.ReadAsync<DenyList>(_denyListPath, cancellationToken);
            return denyList switch
            {
                null => DenyList.Empty,
                { Entries: null } => new ResearchStoreError(_denyListPath, "the deny-list has no entries array."),
                _ => denyList
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ResearchStoreError(_denyListPath, ex.Message);
        }
    }

    private async Task<Result<DenyList, Error>> WriteAsync(DenyList denyList, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_denyListPath)!);
            await JsonFileStore.WriteAsync(_denyListPath, denyList, cancellationToken);
            return denyList;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ResearchStoreError(_denyListPath, ex.Message);
        }
    }
}
