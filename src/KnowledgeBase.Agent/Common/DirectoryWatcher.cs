using System.Collections.Concurrent;

namespace KnowledgeBase.Agent.Common;

/// <summary>
/// Watches a directory for new files and hands each one to a callback, once it is fully written.
/// Files already in the directory when watching starts are handled too, and the directory is rescanned when the
/// watcher reports an error (such as an internal buffer overflow), so no file is missed. Callers must move or delete
/// handled files, otherwise they are handled again on the next start or rescan.
/// </summary>
internal sealed class DirectoryWatcher(
    ILogger logger,
    string directory,
    string filter,
    Func<string, Task> handleFile)
    : IDisposable
{
    private static readonly TimeSpan ReadyRetryDelay = TimeSpan.FromMilliseconds(250);
    private const int ReadyMaxAttempts = 40;

    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;

    public void Start()
    {
        _watcher = new FileSystemWatcher
        {
            Path = directory,
            Filter = filter,
            NotifyFilter = NotifyFilters.FileName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        _watcher.Created += OnCreated;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;

        Rescan();
    }

    public void Dispose()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.Created -= OnCreated;
        _watcher.Renamed -= OnRenamed;
        _watcher.Error -= OnError;
        _watcher.Dispose();
    }

    private void OnCreated(object sender, FileSystemEventArgs e) => Dispatch(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e) => Dispatch(e.FullPath);

    private void OnError(object sender, ErrorEventArgs e)
    {
        logger.DirectoryWatcherFailed(directory, e.GetException());
        Rescan();
    }

    private void Rescan()
    {
        foreach (string filePath in Directory.EnumerateFiles(directory, filter, SearchOption.TopDirectoryOnly).Order())
        {
            Dispatch(filePath);
        }
    }

    private void Dispatch(string filePath)
    {
        if (!_inFlight.TryAdd(filePath, 0))
        {
            return;
        }

        _ = HandleAsync(filePath);
    }

    private async Task HandleAsync(string filePath)
    {
        try
        {
            if (await WaitUntilReady(filePath))
            {
                await handleFile(filePath);
            }
        }
        catch (Exception ex)
        {
            logger.DirectoryWatcherHandlerFailed(filePath, ex);
        }
        finally
        {
            _inFlight.TryRemove(filePath, out _);
        }
    }

    /// <summary>
    /// Waits until the file can be opened exclusively, which means its writer has finished with it.
    /// </summary>
    private async Task<bool> WaitUntilReady(string filePath)
    {
        for (int attempt = 1; attempt <= ReadyMaxAttempts; attempt++)
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                await using FileStream _ = new(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                await Task.Delay(ReadyRetryDelay);
            }
        }

        logger.FileNotReady(filePath);
        return false;
    }
}