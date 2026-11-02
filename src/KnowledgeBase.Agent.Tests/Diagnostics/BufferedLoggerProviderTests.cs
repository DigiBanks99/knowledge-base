using KnowledgeBase.Agent.Diagnostics;

using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.Tests.Diagnostics;

public sealed class BufferedLoggerProviderTests : IDisposable
{
    private readonly LogBuffer _buffer = new(LogBuffer.DefaultCapacity);
    private readonly BufferedLoggerProvider _provider;

    public BufferedLoggerProviderTests() =>
        _provider = new BufferedLoggerProvider(_buffer, TimeProvider.System);

    public void Dispose() => _provider.Dispose();

    [Test]
    public void AnAppMessageAtInformationIsKept()
    {
        ILogger logger = _provider.CreateLogger("KnowledgeBase.Agent.Research.Search.TopicSearch");

        logger.Log(LogLevel.Information, "Searched research topic 'garden': 4 results found, 1 kept for triage.");

        ItShouldKeepTheMessage();

        void ItShouldKeepTheMessage() =>
            _buffer.Query(LogLevel.Trace, null, 10).Select(entry => entry.Message)
                .ShouldBe(["Searched research topic 'garden': 4 results found, 1 kept for triage."]);
    }

    [Test]
    public void AFrameworkMessageAtInformationIsDropped()
    {
        ILogger logger = _provider.CreateLogger("Microsoft.AspNetCore.Routing.EndpointMiddleware");

        logger.Log(LogLevel.Information, "Executing endpoint 'GET /api/status'");

        ItShouldDropTheMessage();

        void ItShouldDropTheMessage() =>
            _buffer.Query(LogLevel.Trace, null, 10).ShouldBeEmpty();
    }

    [Test]
    public void AFrameworkWarningIsKept()
    {
        ILogger logger = _provider.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics");

        logger.Log(LogLevel.Warning, "Slow request.");

        ItShouldKeepTheWarning();

        void ItShouldKeepTheWarning() =>
            _buffer.Query(LogLevel.Trace, null, 10).Select(entry => entry.Level).ShouldBe([LogLevel.Warning]);
    }

    [Test]
    public void AnExceptionIsKeptWithItsStackTrace()
    {
        ILogger logger = _provider.CreateLogger("KnowledgeBase.Agent.Research.Fetch.PendingResultFetch");
        InvalidOperationException exception = new("The page could not be read.");

        logger.Log(LogLevel.Error, exception, "Fetching failed.");

        ItShouldKeepTheExceptionText();

        void ItShouldKeepTheExceptionText() =>
            (_buffer.Query(LogLevel.Trace, null, 10).Single().Exception ?? string.Empty).ShouldContain("The page could not be read.");
    }

    [Test]
    public void AMessageLongerThanTheLimitIsCut()
    {
        ILogger logger = _provider.CreateLogger("KnowledgeBase.Agent.Notes");

        logger.Log(LogLevel.Information, new string('a', 5000));

        ItShouldCutTheMessage();

        void ItShouldCutTheMessage() =>
            _buffer.Query(LogLevel.Trace, null, 10).Single().Message.Length.ShouldBe(4001);
    }
}
