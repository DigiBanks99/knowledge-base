using System.Collections.Concurrent;

using KnowledgeBase.Agent.Common;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.IntegrationTests.Common;

public sealed class DirectoryWatcherTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly ConcurrentQueue<string> _handled = new();
    private readonly DirectoryWatcher _watcher;

    public DirectoryWatcherTests()
    {
        _watcher = new DirectoryWatcher(NullLogger.Instance, TimeProvider.System, _directory.Path, "*.md", HandleAsync);
    }

    /// <summary>Files with this name fail to be handled.</summary>
    private const string BrokenFile = "broken.md";

    public void Dispose()
    {
        _watcher.Dispose();
        _directory.Dispose();
    }

    [Test]
    public async Task FilesAreWaitingWhenWatchingStarts()
    {
        await File.WriteAllTextAsync(_directory.File("a.md"), "A");
        await File.WriteAllTextAsync(_directory.File("b.md"), "B");

        _watcher.Start();
        bool handled = await Eventually.Until(() => _handled.Count == 2);

        ItShouldHandleEveryWaitingFile();

        void ItShouldHandleEveryWaitingFile()
        {
            handled.ShouldBeTrue();
            _handled.ShouldBe(["a.md", "b.md"], ignoreOrder: true);
        }
    }

    [Test]
    public async Task AFileArrives()
    {
        _watcher.Start();

        await File.WriteAllTextAsync(_directory.File("a.md"), "A");
        await File.WriteAllTextAsync(_directory.File("ignored.txt"), "Not markdown.");
        bool handled = await Eventually.Until(() => _handled.Count == 1);

        ItShouldHandleIt();
        ItShouldIgnoreFilesOutsideTheFilter();

        void ItShouldHandleIt() => handled.ShouldBeTrue();

        void ItShouldIgnoreFilesOutsideTheFilter() => _handled.ShouldBe(["a.md"]);
    }

    [Test]
    public async Task AFileIsRenamedToMatchTheFilter()
    {
        _watcher.Start();

        await File.WriteAllTextAsync(_directory.File("a.tmp"), "A");
        File.Move(_directory.File("a.tmp"), _directory.File("a.md"));
        bool handled = await Eventually.Until(() => _handled.Count == 1);

        ItShouldHandleTheRenamedFile();

        void ItShouldHandleTheRenamedFile()
        {
            handled.ShouldBeTrue();
            _handled.ShouldBe(["a.md"]);
        }
    }

    [Test]
    public async Task AFileIsStillBeingWritten()
    {
        bool handledWhileOpen;

        // The writer opens the file before watching starts: on Linux the watcher could otherwise lock a new file before
        // its writer does, which is why the pipeline's own writers move complete files into place.
        await using (FileStream writer = new(_directory.File("a.md"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            await writer.WriteAsync("A"u8.ToArray());
            await writer.FlushAsync();
            _watcher.Start();
            handledWhileOpen = await Eventually.Until(() => !_handled.IsEmpty, TimeSpan.FromMilliseconds(750));
        }

        bool handledOnceClosed = await Eventually.Until(() => _handled.Count == 1);

        ItShouldWaitForTheWriter();
        ItShouldHandleTheFileOnceWritten();

        void ItShouldWaitForTheWriter() => handledWhileOpen.ShouldBeFalse();

        void ItShouldHandleTheFileOnceWritten() => handledOnceClosed.ShouldBeTrue();
    }

    [Test]
    public async Task AHandlerFails()
    {
        _watcher.Start();

        await File.WriteAllTextAsync(_directory.File(BrokenFile), "A");
        await Eventually.Until(() => _handled.Count == 1);
        await File.WriteAllTextAsync(_directory.File("b.md"), "B");
        bool handled = await Eventually.Until(() => _handled.Count == 2);

        ItShouldKeepWatching();

        void ItShouldKeepWatching()
        {
            handled.ShouldBeTrue();
            _handled.ShouldBe([BrokenFile, "b.md"]);
        }
    }

    /// <summary>Behaves like the pipeline's handlers: records the file and moves it on, here by deleting it.</summary>
    private Task HandleAsync(string path)
    {
        string name = Path.GetFileName(path);
        _handled.Enqueue(name);
        File.Delete(path);
        return name == BrokenFile ? throw new IOException("The disk is full.") : Task.CompletedTask;
    }
}