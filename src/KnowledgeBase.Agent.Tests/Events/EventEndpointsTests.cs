using System.Net.ServerSentEvents;

using KnowledgeBase.Agent.Events;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Events;

public sealed class EventEndpointsTests : IDisposable
{
    private readonly ChangeNotifier _notifier = new(
        NullLogger<ChangeNotifier>.Instance,
        Options.Create(new WikiConfigurationSettings { RootDirectory = Path.GetTempPath() }),
        TimeProvider.System);

    public void Dispose() => _notifier.Dispose();

    [Test]
    public async Task TheClientDisconnectsBetweenChanges()
    {
        using CancellationTokenSource disconnect = new();
        IAsyncEnumerator<SseItem<ChangeEvent>> stream = EventEndpoints
            .Stream(_notifier, TimeProvider.System, disconnect.Token)
            .GetAsyncEnumerator(disconnect.Token);
        await stream.MoveNextAsync();
        string first = stream.Current.EventType!;

        Exception? exception = await Catch.ExceptionAsync(async () => await stream.DisposeAsync());

        ItShouldHaveOpenedWithAPing();
        ItShouldCloseQuietly();

        void ItShouldHaveOpenedWithAPing() => first.ShouldBe("ping");

        void ItShouldCloseQuietly() => exception.ShouldBeNull();
    }
}
