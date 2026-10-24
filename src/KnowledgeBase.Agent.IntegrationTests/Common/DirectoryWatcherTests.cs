using System.Collections.Concurrent;

using KnowledgeBase.Agent.Common;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.IntegrationTests.Common;

public sealed class DirectoryWatcherTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly ConcurrentQueue<string> _handled = new();
    private readonly ConcurrentDictionary<string, string> _contents = new();
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
    public async Task AFileIsStillGrowing()
    {
        string path = _directory.File("a.md");
        _watcher.Start();
        bool handledWhileGrowing = false;

        // Another program writing slowly, such as a copy over the network: the file exists from the first chunk.
        await using (FileStream writer = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            for (int chunk = 1; chunk <= 10; chunk++)
            {
                await writer.WriteAsync(System.Text.Encoding.UTF8.GetBytes($"Line {chunk}.\n"));
                await writer.FlushAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                handledWhileGrowing |= !_handled.IsEmpty;
            }
        }

        bool handledOnceWritten = await Eventually.Until(() => _handled.Count == 1);

        ItShouldWaitUntilTheFileStopsGrowing();
        ItShouldHandleTheWholeFile();

        void ItShouldWaitUntilTheFileStopsGrowing() => handledWhileGrowing.ShouldBeFalse();

        void ItShouldHandleTheWholeFile()
        {
            handledOnceWritten.ShouldBeTrue();
            _contents["a.md"].ShouldEndWith("Line 10.\n");
        }
    }

    [Test]
    public async Task AnEmptyFileArrives()
    {
        string path = _directory.File("a.md");
        _watcher.Start();

        await File.WriteAllTextAsync(path, string.Empty);
        bool handledWhileEmpty = await Eventually.Until(() => !_handled.IsEmpty, TimeSpan.FromMilliseconds(1_000));
        await File.WriteAllTextAsync(path, "Written at last.");
        bool handledOnceWritten = await Eventually.Until(() => _handled.Count == 1);

        ItShouldWaitForContent();
        ItShouldHandleTheContent();

        void ItShouldWaitForContent() => handledWhileEmpty.ShouldBeFalse();

        void ItShouldHandleTheContent()
        {
            handledOnceWritten.ShouldBeTrue();
            _contents["a.md"].ShouldBe("Written at last.");
        }
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

    /// <summary>Behaves like the pipeline's handlers: reads the file and moves it on, here by deleting it.</summary>
    private Task HandleAsync(string path)
    {
        string name = Path.GetFileName(path);
        _contents[name] = File.ReadAllText(path);
        _handled.Enqueue(name);
        File.Delete(path);
        return name == BrokenFile ? throw new IOException("The disk is full.") : Task.CompletedTask;
    }
}