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
    TimeProvider timeProvider,
    string directory,
    string filter,
    Func<string, Task> handleFile)
    : IDisposable
{
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(1);
    private const int ReadyStablePolls = 2;

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

    /// <remarks>
    /// Runs on a thread-pool callback, where an exception would end the process. Windows reports an error when the
    /// directory itself goes (deleted, or its volume lost), so the rescan that follows can fail and is only logged.
    /// </remarks>
    private void OnError(object sender, ErrorEventArgs e)
    {
        logger.DirectoryWatcherFailed(directory, e.GetException());
        try
        {
            Rescan();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.DirectoryWatcherRescanFailed(directory, ex);
        }
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
    /// Waits until the file has content and its size and last write time have not changed for
    /// <see cref="ReadyStablePolls"/> polls in a row, which means its writer has finished with it.
    /// </summary>
    /// <remarks>
    /// The app moves the files it writes into place complete, so this is for files written by other programs, such as a
    /// note dropped into the folder by hand. An exclusive open is no test on Linux, where file locks are advisory: other
    /// programs do not take them, and a .NET writer takes its lock just after creating the file, so a watcher holding
    /// one at that moment makes the writer fail. Windows locks are enforced, so there a stable file must also open for
    /// reading: a writer that paused can still have it open, and a virus scanner opens a file once it is written, and
    /// either would make the handler's read fail.
    /// </remarks>
    private async Task<bool> WaitUntilReady(string filePath)
    {
        long started = timeProvider.GetTimestamp();
        (long Length, DateTime LastWriteTime)? previous = null;
        int stablePolls = 0;
        while (timeProvider.GetElapsedTime(started) < ReadyTimeout)
        {
            FileInfo file = new(filePath);
            if (!file.Exists)
            {
                return false;
            }

            (long Length, DateTime LastWriteTime) current = (file.Length, file.LastWriteTimeUtc);
            stablePolls = current.Length > 0 && current == previous ? stablePolls + 1 : 0;
            if (stablePolls >= ReadyStablePolls)
            {
                if (!OperatingSystem.IsWindows() || CanBeRead(filePath))
                {
                    return true;
                }

                stablePolls = 0;
            }

            previous = current;
            await Task.Delay(ReadyPollInterval, timeProvider);
        }

        logger.FileNotReady(filePath);
        return false;
    }

    private static bool CanBeRead(string filePath)
    {
        try
        {
            using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
