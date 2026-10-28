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
    private readonly string _topicsDirectory = settings.Value.ResearchTopicsDirectory;
    private readonly string _curationRequestsDirectory = settings.Value.CurationRequestsDirectory;
    private readonly string _curationFailedDirectory = settings.Value.CurationFailedDirectory;
    private readonly string _pendingBatchesDirectory = settings.Value.PendingBatchesDirectory;
    private readonly string _awaitingBatchesDirectory = settings.Value.AwaitingBatchesDirectory;
    private readonly string _completedBatchesDirectory = settings.Value.CompletedBatchesDirectory;
    private readonly string _blockedDomainsPath = settings.Value.BlockedDomainsPath;
    private readonly SemaphoreSlim _denyListLock = new(1, 1);

    // One lock for topics and their curation requests, so pruning a topic never races a request being merged for it.
    private readonly SemaphoreSlim _topicsLock = new(1, 1);

    public void Dispose()
    {
        _denyListLock.Dispose();
        _topicsLock.Dispose();
    }

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

    public Task<Result<IReadOnlyList<ResearchTopic>, Error>> ListTopicsAsync(CancellationToken cancellationToken) =>
        ReadAllAsync<ResearchTopic>(_topicsDirectory, cancellationToken);

    public async Task<Result<Maybe<ResearchTopic>, Error>> UpdateTopicAsync(
        string topic,
        Func<Maybe<ResearchTopic>, Result<Maybe<ResearchTopic>, Error>> update,
        CancellationToken cancellationToken)
    {
        string topicPath = TopicFilePath(_topicsDirectory, topic);
        await _topicsLock.WaitAsync(cancellationToken);
        try
        {
            Result<Maybe<ResearchTopic>, Error> current = await ReadDocumentAsync<ResearchTopic>(topicPath, cancellationToken);
            if (current.IsFailure)
            {
                return current;
            }

            Result<Maybe<ResearchTopic>, Error> updated = update(current.Value);
            if (updated.IsFailure || Equals(updated.Value, current.Value))
            {
                return updated;
            }

            return updated.Value.HasValue
                ? await WriteDocumentAsync(topicPath, updated.Value.Value, cancellationToken).Map(written => Maybe.From(written))
                : Delete(topicPath)
                    .Bind(() => Delete(TopicFilePath(_curationRequestsDirectory, topic)))
                    .Bind(() => Delete(TopicFilePath(_curationFailedDirectory, topic)))
                    .Bind(() => Delete(TopicFilePath(_pendingBatchesDirectory, topic)))
                    .Map(() => Maybe<ResearchTopic>.None);
        }
        finally
        {
            _topicsLock.Release();
        }
    }

    public Task<Result<IReadOnlyList<CurationRequest>, Error>> ListCurationRequestsAsync(CancellationToken cancellationToken) =>
        ReadAllAsync<CurationRequest>(_curationRequestsDirectory, cancellationToken);

    public async Task<Result<CurationRequest, Error>> RequestCurationAsync(
        CurationRequest request,
        CancellationToken cancellationToken)
    {
        string requestPath = TopicFilePath(_curationRequestsDirectory, request.Topic);
        await _topicsLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadDocumentAsync<CurationRequest>(requestPath, cancellationToken)
                .Map(waiting => waiting.HasValue ? waiting.Value.Merge(request) : request)
                .Bind(merged => WriteDocumentAsync(requestPath, merged, cancellationToken));
        }
        finally
        {
            _topicsLock.Release();
        }
    }

    public async Task<UnitResult<Error>> CompleteCurationAsync(CurationRequest curated, CancellationToken cancellationToken)
    {
        await _topicsLock.WaitAsync(cancellationToken);
        try
        {
            return await RemoveRequestUnlessChangedAsync(curated, cancellationToken)
                .Bind(() => Delete(TopicFilePath(_curationFailedDirectory, curated.Topic)));
        }
        finally
        {
            _topicsLock.Release();
        }
    }

    public async Task<UnitResult<Error>> FailCurationAsync(FailedCuration failure, CancellationToken cancellationToken)
    {
        await _topicsLock.WaitAsync(cancellationToken);
        try
        {
            Result<FailedCuration, Error> written = await WriteDocumentAsync(
                TopicFilePath(_curationFailedDirectory, failure.Request.Topic),
                failure,
                cancellationToken);
            return written.IsFailure
                ? written.Error
                : await RemoveRequestUnlessChangedAsync(failure.Request, cancellationToken);
        }
        finally
        {
            _topicsLock.Release();
        }
    }

    public Task<Result<IReadOnlyList<FailedCuration>, Error>> ListFailedCurationsAsync(CancellationToken cancellationToken) =>
        ReadAllAsync<FailedCuration>(_curationFailedDirectory, cancellationToken);

    public Task<Result<Maybe<ResearchTopic>, Error>> ReadTopicAsync(string topic, CancellationToken cancellationToken) =>
        ReadDocumentAsync<ResearchTopic>(TopicFilePath(_topicsDirectory, topic), cancellationToken);

    public Task<Result<IReadOnlySet<string>, Error>> ListTopicsInFlightAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlySet<string> topics = new[] { _pendingBatchesDirectory, _awaitingBatchesDirectory }
                .Where(Directory.Exists)
                .SelectMany(directory => Directory.EnumerateFiles(directory, "*.json"))
                .Select(filePath => TopicPaths.FromSlug(Path.GetFileNameWithoutExtension(filePath)))
                .Choose()
                .ToHashSet(StringComparer.Ordinal);
            return Task.FromResult(Result.Success<IReadOnlySet<string>, Error>(topics));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Result.Failure<IReadOnlySet<string>, Error>(new ResearchStoreError(_pendingBatchesDirectory, ex.Message)));
        }
    }

    public Task<Result<IReadOnlyList<ResearchBatch>, Error>> ListPendingBatchesAsync(CancellationToken cancellationToken) =>
        ReadAllAsync<ResearchBatch>(_pendingBatchesDirectory, cancellationToken);

    public Task<Result<Maybe<ResearchBatch>, Error>> ReadPendingBatchAsync(string topic, CancellationToken cancellationToken) =>
        ReadDocumentAsync<ResearchBatch>(TopicFilePath(_pendingBatchesDirectory, topic), cancellationToken);

    public async Task<Result<Maybe<ResearchBatch>, Error>> WritePendingBatchAsync(
        ResearchBatch batch,
        CancellationToken cancellationToken)
    {
        // Under the topics lock, so a topic pruned while it was being searched never gets an orphaned batch.
        await _topicsLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(TopicFilePath(_topicsDirectory, batch.Topic)))
            {
                return Maybe<ResearchBatch>.None;
            }

            return await WriteDocumentAsync(TopicFilePath(_pendingBatchesDirectory, batch.Topic), batch, cancellationToken)
                .Map(written => Maybe.From(written));
        }
        finally
        {
            _topicsLock.Release();
        }
    }

    public async Task<Result<Maybe<ResearchBatch>, Error>> UpdatePendingBatchAsync(
        string topic,
        Func<ResearchBatch, ResearchBatch> update,
        CancellationToken cancellationToken)
    {
        // Under the topics lock, so a batch pruned with its topic is never written back.
        string batchPath = TopicFilePath(_pendingBatchesDirectory, topic);
        await _topicsLock.WaitAsync(cancellationToken);
        try
        {
            Result<Maybe<ResearchBatch>, Error> current = await ReadDocumentAsync<ResearchBatch>(batchPath, cancellationToken);
            if (current.IsFailure || current.Value.HasNoValue)
            {
                return current;
            }

            return await WriteDocumentAsync(batchPath, update(current.Value.Value), cancellationToken)
                .Map(written => Maybe.From(written));
        }
        finally
        {
            _topicsLock.Release();
        }
    }

    public async Task<Result<Maybe<AwaitingBatch>, Error>> AwaitBatchAsync(
        string topic,
        string noteFile,
        DateTimeOffset notedAt,
        CancellationToken cancellationToken)
    {
        // The awaiting file is written before the pending one is removed, so a crash between them leaves the batch in
        // both places (resolved as pending again, which writes the same note) rather than in neither.
        string pendingPath = TopicFilePath(_pendingBatchesDirectory, topic);
        await _topicsLock.WaitAsync(cancellationToken);
        try
        {
            Result<Maybe<ResearchBatch>, Error> pending = await ReadDocumentAsync<ResearchBatch>(pendingPath, cancellationToken);
            if (pending.IsFailure || pending.Value.HasNoValue)
            {
                return pending.IsFailure ? pending.Error : Maybe<AwaitingBatch>.None;
            }

            return await WriteDocumentAsync(
                    TopicFilePath(_awaitingBatchesDirectory, topic),
                    new AwaitingBatch(pending.Value.Value, noteFile, notedAt),
                    cancellationToken)
                .Bind(written => Delete(pendingPath).Map(() => Maybe.From(written)));
        }
        finally
        {
            _topicsLock.Release();
        }
    }

    public async Task<Result<Maybe<ResearchBatch>, Error>> CompleteBatchAsync(string topic, CancellationToken cancellationToken)
    {
        string pendingPath = TopicFilePath(_pendingBatchesDirectory, topic);
        await _topicsLock.WaitAsync(cancellationToken);
        try
        {
            Result<Maybe<ResearchBatch>, Error> pending = await ReadDocumentAsync<ResearchBatch>(pendingPath, cancellationToken);
            if (pending.IsFailure || pending.Value.HasNoValue)
            {
                return pending;
            }

            ResearchBatch batch = pending.Value.Value;
            string completedPath = Path.Combine(
                _completedBatchesDirectory,
                $"{TopicPaths.Slug(topic)}-{batch.SearchedAt.UtcDateTime:yyyyMMdd-HHmmss}.json");
            return await WriteDocumentAsync(completedPath, batch, cancellationToken)
                .Bind(written => Delete(pendingPath).Map(() => Maybe.From(written)));
        }
        finally
        {
            _topicsLock.Release();
        }
    }

    /// <summary>A corrupt list is an error rather than none, so blocked domains are never fetched by mistake.</summary>
    public async Task<Result<BlockedDomains, Error>> ReadBlockedDomainsAsync(CancellationToken cancellationToken) =>
        await ReadDocumentAsync<BlockedDomains>(_blockedDomainsPath, cancellationToken)
            .Bind(read => read.HasValue && read.Value.Blocked is null
                ? Result.Failure<BlockedDomains, Error>(new ResearchStoreError(_blockedDomainsPath, "the domain list has no blocked array."))
                : Result.Success<BlockedDomains, Error>(read.GetValueOrDefault(BlockedDomains.None)));

    /// <summary>Called under the topics lock, so no request can be merged between the read and the delete.</summary>
    private async Task<UnitResult<Error>> RemoveRequestUnlessChangedAsync(CurationRequest handled, CancellationToken cancellationToken)
    {
        string requestPath = TopicFilePath(_curationRequestsDirectory, handled.Topic);
        Result<Maybe<CurationRequest>, Error> waiting = await ReadDocumentAsync<CurationRequest>(requestPath, cancellationToken);
        if (waiting.IsFailure)
        {
            return waiting.Error;
        }

        return waiting.Value.HasValue && waiting.Value.Value.IsSameAs(handled)
            ? Delete(requestPath)
            : UnitResult.Success<Error>();
    }

    private static string TopicFilePath(string directory, string topic) =>
        Path.Combine(directory, $"{TopicPaths.Slug(topic)}.json");

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

    private async Task<Result<DenyList, Error>> WriteAsync(DenyList denyList, CancellationToken cancellationToken) =>
        await WriteDocumentAsync(_denyListPath, denyList, cancellationToken);

    private static async Task<Result<IReadOnlyList<T>, Error>> ReadAllAsync<T>(
        string directory,
        CancellationToken cancellationToken)
    {
        try
        {
            return Directory.Exists(directory)
                ? Result.Success<IReadOnlyList<T>, Error>(await JsonFileStore.ReadAllAsync<T>(directory, cancellationToken))
                : Result.Success<IReadOnlyList<T>, Error>([]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ResearchStoreError(directory, ex.Message);
        }
    }

    /// <summary>A document that cannot be read is an error, so an update never overwrites state it could not see.</summary>
    private static async Task<Result<Maybe<T>, Error>> ReadDocumentAsync<T>(string filePath, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return Maybe.From(await JsonFileStore.ReadAsync<T>(filePath, cancellationToken));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ResearchStoreError(filePath, ex.Message);
        }
    }

    private static async Task<Result<T, Error>> WriteDocumentAsync<T>(string filePath, T document, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await JsonFileStore.WriteAsync(filePath, document, cancellationToken);
            return document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ResearchStoreError(filePath, ex.Message);
        }
    }

    private static UnitResult<Error> Delete(string filePath)
    {
        try
        {
            // File.Delete ignores a missing file but not a missing folder, and the curation folders may not exist yet.
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            return UnitResult.Success<Error>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ResearchStoreError(filePath, ex.Message);
        }
    }
}
