using System.Collections.Concurrent;
using System.Threading.Channels;

using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Events;

/// <summary>An area of the knowledge base whose files changed, such as <c>notes</c>, <c>runs</c> or <c>wiki</c>.</summary>
public sealed record ChangeEvent(string Area, DateTimeOffset At);

/// <summary>
/// Watches the whole root directory and tells subscribers (the UI's event stream) which areas changed. Everything the
/// pipeline does is a file change, so this one watcher covers notes, facts, runs, findings, the wiki and manual edits.
/// Changes are debounced per area so a burst of writes becomes one event.
/// </summary>
internal sealed class ChangeNotifier(
    ILogger<ChangeNotifier> logger,
    IOptions<WikiConfigurationSettings> settings,
    TimeProvider timeProvider)
    : IChangePublisher, IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    public static readonly string[] Areas = ["notes", "facts", "review", "wiki", "runs", "conversations"];

    private readonly string _rootDirectory = Path.GetFullPath(settings.Value.RootDirectory);
    private readonly ConcurrentDictionary<Guid, Channel<ChangeEvent>> _subscribers = new();
    private readonly ConcurrentDictionary<string, byte> _pendingAreas = new();
    private FileSystemWatcher? _watcher;

    public void Start()
    {
        _watcher = new FileSystemWatcher(_rootDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.Error += OnError;
    }

    public async IAsyncEnumerable<ChangeEvent> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guid id = Guid.NewGuid();
        Channel<ChangeEvent> channel = Channel.CreateBounded<ChangeEvent>(
            new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest });
        _subscribers[id] = channel;
        try
        {
            await foreach (ChangeEvent change in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return change;
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }

    public void Publish(string area) => Notify(area);

    public void Dispose()
    {
        _watcher?.Dispose();
        foreach (Channel<ChangeEvent> channel in _subscribers.Values)
        {
            channel.Writer.TryComplete();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        string relativePath = Path.GetRelativePath(_rootDirectory, e.FullPath);
        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string area = segments[0];

        // Every commit rewrites the wiki repository's index and objects; that is not a change anyone needs to see.
        if (Areas.Contains(area) && !segments.Skip(1).Any(WikiPaths.IsRepositoryName))
        {
            Notify(area);
        }
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        logger.ChangeWatcherFailed(e.GetException());
        foreach (string area in Areas)
        {
            Notify(area);
        }
    }

    private void Notify(string area)
    {
        if (!_pendingAreas.TryAdd(area, 0))
        {
            return;
        }

        _ = PublishAfterDebounce(area);
    }

    private async Task PublishAfterDebounce(string area)
    {
        await Task.Delay(DebounceDelay, timeProvider);
        _pendingAreas.TryRemove(area, out _);

        ChangeEvent change = new(area, timeProvider.GetUtcNow());
        foreach (Channel<ChangeEvent> channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(change);
        }
    }
}