using KnowledgeBase.Agent.Common;

namespace KnowledgeBase.Agent.IntegrationTests.Common;

public sealed class JsonFileStoreTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Test]
    public async Task ADocumentIsReplaced()
    {
        string path = _directory.File("document.json");

        await JsonFileStore.WriteAsync(path, new Document("first", 1), CancellationToken.None);
        await JsonFileStore.WriteAsync(path, new Document("second", 2), CancellationToken.None);
        Document? read = await JsonFileStore.ReadAsync<Document>(path, CancellationToken.None);

        ItShouldReadTheLatestVersion();
        ItShouldLeaveNoTemporaryFilesBehind();

        void ItShouldReadTheLatestVersion() => read.ShouldBe(new Document("second", 2));

        void ItShouldLeaveNoTemporaryFilesBehind() => TestWiki.FileNames(_directory.Path).ShouldBe(["document.json"]);
    }

    [Test]
    public async Task AMissingDocumentIsRead()
    {
        Document? read = await JsonFileStore.ReadAsync<Document>(_directory.File("missing.json"), CancellationToken.None);

        ItShouldFindNothing();

        void ItShouldFindNothing() => read.ShouldBeNull();
    }

    [Test]
    public async Task ACorruptDocumentIsAmongOthers()
    {
        await JsonFileStore.WriteAsync(_directory.File("a.json"), new Document("a", 1), CancellationToken.None);
        await File.WriteAllTextAsync(_directory.File("b.json"), "{ \"name\": \"half-writ");
        await JsonFileStore.WriteAsync(_directory.File("c.json"), new Document("c", 1), CancellationToken.None);

        IReadOnlyList<Document> documents = await JsonFileStore.ReadAllAsync<Document>(_directory.Path, CancellationToken.None);

        ItShouldSkipOnlyTheCorruptDocument();

        void ItShouldSkipOnlyTheCorruptDocument() =>
            documents.Select(document => document.Name).ShouldBe(["a", "c"], ignoreOrder: true);
    }

    private sealed record Document(string Name, int Version);
}
