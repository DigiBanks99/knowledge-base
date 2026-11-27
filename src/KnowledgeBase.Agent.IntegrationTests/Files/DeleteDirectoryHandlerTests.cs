using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Files.Errors;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.IntegrationTests.Files;

public sealed class DeleteDirectoryHandlerTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly DeleteDirectoryHandler _handler = new(NullLogger<DeleteDirectoryHandler>.Instance);

    public void Dispose() => _directory.Dispose();

    [Test]
    public void AnEmptyDirectoryIsDeleted()
    {
        string directory = Directory.CreateDirectory(_directory.File("knowlege-base")).FullName;

        Result<bool, Error> deleted = _handler.Handle(new DeleteDirectoryRequest(directory));

        ItShouldRemoveTheDirectory();

        void ItShouldRemoveTheDirectory()
        {
            deleted.IsSuccess.ShouldBeTrue();
            Directory.Exists(directory).ShouldBeFalse();
        }
    }

    [Test]
    public async Task ADirectoryStillHoldsPages()
    {
        string directory = Directory.CreateDirectory(_directory.File("knowlege-base")).FullName;
        await File.WriteAllTextAsync(Path.Combine(directory, "index.md"), "# Knowledge base");
        Directory.CreateDirectory(Path.Combine(directory, "notes"));

        Result<bool, Error> deleted = _handler.Handle(new DeleteDirectoryRequest(directory));

        ItShouldBeRefusedNamingWhatIsLeft();
        ItShouldLeaveTheDirectoryAndItsPages();

        void ItShouldBeRefusedNamingWhatIsLeft() => deleted.Error.ShouldBeOfType<DirectoryNotEmptyError>().ShouldSatisfyAllConditions(
            error => error.Entries.ShouldBe(["index.md", "notes/"]),
            error => error.Message.ShouldContain("`MoveFile`", Case.Sensitive));

        void ItShouldLeaveTheDirectoryAndItsPages() => File.Exists(Path.Combine(directory, "index.md")).ShouldBeTrue();
    }

    [Test]
    public void ADirectoryDoesNotExist()
    {
        string directory = _directory.File("missing");

        Result<bool, Error> deleted = _handler.Handle(new DeleteDirectoryRequest(directory));

        ItShouldBeNotFound();

        void ItShouldBeNotFound() => deleted.Error.ShouldBeOfType<DirectoryNotFoundError>();
    }
}
