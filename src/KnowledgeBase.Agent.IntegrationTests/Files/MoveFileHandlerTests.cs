using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Files.Errors;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.IntegrationTests.Files;

public sealed class MoveFileHandlerTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly MoveFileHandler _handler = new(NullLogger<MoveFileHandler>.Instance);

    public void Dispose() => _directory.Dispose();

    [Test]
    public async Task AFileIsOpenElsewhereForAMoment()
    {
        string source = _directory.File("a.md");
        string destination = _directory.File("b.md");
        await File.WriteAllTextAsync(source, "A");

        // Another program, such as a virus scanner, looking at the file as it is claimed. Windows refuses the move
        // while it has the file open; Linux allows it.
        Result<string, Error> moved;
        Task released;
        using (FileStream scanner = new(source, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            released = Task.Delay(TimeSpan.FromMilliseconds(100)).ContinueWith(_ => scanner.Dispose(), TaskScheduler.Default);
            moved = await _handler.Handle(new MoveFileRequest(source, destination), CancellationToken.None);
        }

        await released;

        ItShouldMoveTheFileOnceItIsFree();

        void ItShouldMoveTheFileOnceItIsFree()
        {
            moved.IsSuccess.ShouldBeTrue();
            File.Exists(source).ShouldBeFalse();
            File.ReadAllText(destination).ShouldBe("A");
        }
    }

    [Test]
    public async Task AFileStaysOpenElsewhere()
    {
        string source = _directory.File("a.md");
        string destination = _directory.File("b.md");
        await File.WriteAllTextAsync(source, "A");

        Result<string, Error> moved;
        using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            moved = await _handler.Handle(new MoveFileRequest(source, destination), CancellationToken.None);
        }

        ItShouldGiveUpAndLeaveTheFile();

        void ItShouldGiveUpAndLeaveTheFile()
        {
            // Linux locks are advisory, so there the move goes ahead.
            if (OperatingSystem.IsWindows())
            {
                moved.Error.ShouldBeOfType<FileMoveError>();
                File.Exists(source).ShouldBeTrue();
            }
        }
    }
}
