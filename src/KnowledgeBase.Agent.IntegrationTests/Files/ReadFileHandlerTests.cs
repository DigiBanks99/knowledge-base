using KnowledgeBase.Agent.Files;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.IntegrationTests.Files;

public sealed class ReadFileHandlerTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly ReadFileHandler _handler = new(NullLogger<ReadFileHandler>.Instance);

    public void Dispose() => _directory.Dispose();

    [Test]
    public async Task AFileIsOpenElsewhereForAMoment()
    {
        string path = _directory.File("a.md");
        await File.WriteAllTextAsync(path, "A");

        // Its writer still finishing with it, sharing nothing. .NET refuses the read meanwhile, on Linux too.
        Result<string, Error> read;
        Task released;
        using (FileStream writer = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            released = Task.Delay(TimeSpan.FromMilliseconds(100)).ContinueWith(_ => writer.Dispose(), TaskScheduler.Default);
            read = await _handler.Handle(new ReadFileRequest(path), CancellationToken.None);
        }

        await released;

        ItShouldReadTheFileOnceItIsFree();

        void ItShouldReadTheFileOnceItIsFree() => read.Value.ShouldBe("A");
    }
}
