using KnowledgeBase.Agent.Diagnostics;

using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.Tests.Diagnostics;

public sealed class LogBufferTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly LogBuffer _buffer = new(3);

    [Test]
    public void TheOldestEntriesAreDroppedWhenTheBufferIsFull()
    {
        _buffer.Add(Now, LogLevel.Information, "KnowledgeBase.Agent.One", "first", null);
        _buffer.Add(Now, LogLevel.Information, "KnowledgeBase.Agent.One", "second", null);
        _buffer.Add(Now, LogLevel.Information, "KnowledgeBase.Agent.One", "third", null);
        _buffer.Add(Now, LogLevel.Information, "KnowledgeBase.Agent.One", "fourth", null);

        IReadOnlyList<LogEntry> entries = _buffer.Query(LogLevel.Trace, null, 10);

        ItShouldKeepOnlyTheNewestThree(entries);

        static void ItShouldKeepOnlyTheNewestThree(IReadOnlyList<LogEntry> kept) =>
            kept.Select(entry => entry.Message).ShouldBe(["fourth", "third", "second"]);
    }

    [Test]
    public void TheEntriesAreReturnedNewestFirstWithTheirSequence()
    {
        _buffer.Add(Now, LogLevel.Warning, "KnowledgeBase.Agent.One", "first", null);
        _buffer.Add(Now, LogLevel.Warning, "KnowledgeBase.Agent.One", "second", null);

        IReadOnlyList<LogEntry> entries = _buffer.Query(LogLevel.Trace, null, 10);

        ItShouldListTheNewestFirst(entries);

        static void ItShouldListTheNewestFirst(IReadOnlyList<LogEntry> ordered)
        {
            ordered.Select(entry => entry.Sequence).ShouldBe([2L, 1L]);
        }
    }

    [Test]
    public void OnlyEntriesAtOrAboveTheLevelAreReturned()
    {
        _buffer.Add(Now, LogLevel.Information, "KnowledgeBase.Agent.One", "routine", null);
        _buffer.Add(Now, LogLevel.Error, "KnowledgeBase.Agent.One", "broken", null);

        IReadOnlyList<LogEntry> entries = _buffer.Query(LogLevel.Warning, null, 10);

        ItShouldReturnOnlyTheError(entries);

        static void ItShouldReturnOnlyTheError(IReadOnlyList<LogEntry> matched) =>
            matched.Select(entry => entry.Message).ShouldBe(["broken"]);
    }

    [Test]
    public void ASearchMatchesTheExceptionIgnoringCase()
    {
        _buffer.Add(Now, LogLevel.Error, "KnowledgeBase.Agent.Research", "The search failed.", "System.TimeoutException: Connection to Brave timed out");
        _buffer.Add(Now, LogLevel.Error, "KnowledgeBase.Agent.Research", "Something else failed.", null);

        IReadOnlyList<LogEntry> entries = _buffer.Query(LogLevel.Trace, "brave", 10);

        ItShouldReturnOnlyTheMatchingEntry(entries);

        static void ItShouldReturnOnlyTheMatchingEntry(IReadOnlyList<LogEntry> matched) =>
            matched.Select(entry => entry.Message).ShouldBe(["The search failed."]);
    }

    [Test]
    public void TheLimitCapsTheNumberReturned()
    {
        _buffer.Add(Now, LogLevel.Information, "KnowledgeBase.Agent.One", "first", null);
        _buffer.Add(Now, LogLevel.Information, "KnowledgeBase.Agent.One", "second", null);
        _buffer.Add(Now, LogLevel.Information, "KnowledgeBase.Agent.One", "third", null);

        IReadOnlyList<LogEntry> entries = _buffer.Query(LogLevel.Trace, null, 2);

        ItShouldReturnTheTwoNewest(entries);

        static void ItShouldReturnTheTwoNewest(IReadOnlyList<LogEntry> limited) =>
            limited.Select(entry => entry.Message).ShouldBe(["third", "second"]);
    }
}
