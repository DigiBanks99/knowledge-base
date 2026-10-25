using KnowledgeBase.Agent.Files;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Files;

/// <summary>
/// Only requests that fail validation are tested here: validation happens before the handler touches the disk.
/// </summary>
public sealed class CreateFileHandlerTests
{
    private readonly CreateFileHandler _handler;

    public CreateFileHandlerTests()
    {
        _handler = new CreateFileHandler(
            NullLogger<CreateFileHandler>.Instance,
            new CreateDirectoryHandler(NullLogger<CreateDirectoryHandler>.Instance));
    }

    [Test]
    public async Task AFileOfAnUnsupportedTypeIsCreated()
    {
        CreateFileRequest request = new(Path.Combine("people", "susan"), "# Susan", "text/html");

        Result<string, Error> result = await _handler.Handle(request, CancellationToken.None);

        ItShouldRejectTheRequest();
        ItShouldNameTheSupportedTypes();

        void ItShouldRejectTheRequest() => result.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("MimeTypeUnsupported");

        void ItShouldNameTheSupportedTypes() => result.Error.Message.ShouldBe(
            "The MIME type 'text/html' is not supported. Use one of: text/plain, text/markdown, text/log, application/json.");
    }

    [Test]
    [Arguments("", "# Susan", "text/markdown", "NameRequired")]
    [Arguments("people/susan", " ", "text/markdown", "ContentRequired")]
    [Arguments("people/susan", "# Susan", "", "MimeTypeRequired")]
    public async Task AnIncompleteFileIsCreated(string name, string content, string mimeType, string expectedCode)
    {
        Result<string, Error> result = await _handler.Handle(new CreateFileRequest(name, content, mimeType), CancellationToken.None);

        ItShouldRejectTheRequest();

        void ItShouldRejectTheRequest() => result.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe(expectedCode);
    }
}
