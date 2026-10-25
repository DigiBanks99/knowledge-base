using System.Collections.Concurrent;
using System.Net.Mime;

using KnowledgeBase.Agent.Common;
using KnowledgeBase.Agent.Files;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.IntegrationTests.Files;

public sealed class CreateFileHandlerTests : IDisposable
{
    private const int FileCount = 25;

    private readonly TemporaryDirectory _directory = new();
    private readonly CreateFileHandler _handler = new(
        NullLogger<CreateFileHandler>.Instance,
        new CreateDirectoryHandler(NullLogger<CreateDirectoryHandler>.Instance));

    public void Dispose() => _directory.Dispose();

    [Test]
    public async Task FilesAreCreatedInAWatchedFolder()
    {
        // Large enough that a write is not over before a watcher reacts to the file being created.
        string content = string.Concat(Enumerable.Repeat("Susan gave feedback on the design review. ", 2_000));
        ConcurrentDictionary<string, string> read = new();
        using DirectoryWatcher watcher = new(NullLogger.Instance, TimeProvider.System, _directory.Path, "*.md", async path =>
            read[Path.GetFileName(path)] = await File.ReadAllTextAsync(path));
        watcher.Start();

        Result<string, Error>[] created = await Task.WhenAll(Enumerable.Range(1, FileCount).Select(index =>
            _handler.Handle(new CreateFileRequest(_directory.File($"note-{index}"), content, MediaTypeNames.Text.Markdown), CancellationToken.None)));
        bool allRead = await Eventually.Until(() => read.Count == FileCount);

        ItShouldCreateEveryFile();
        ItShouldOnlyEverShowTheWatcherCompleteFiles();
        ItShouldLeaveNoTemporaryFilesBehind();

        void ItShouldCreateEveryFile() => created.ShouldAllBe(result => result.IsSuccess);

        void ItShouldOnlyEverShowTheWatcherCompleteFiles()
        {
            allRead.ShouldBeTrue();
            read.Values.ShouldAllBe(text => text == content);
        }

        void ItShouldLeaveNoTemporaryFilesBehind() => TestWiki.FileNames(_directory.Path, "*.tmp").ShouldBeEmpty();
    }
}
