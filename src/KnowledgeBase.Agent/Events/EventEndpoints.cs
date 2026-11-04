using System.Net.ServerSentEvents;

namespace KnowledgeBase.Agent.Events;

internal static class EventEndpoints
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapEventEndpoints()
        {
            endpoints
                .MapGet("events", Handle)
                .WithTags("events")
                .WithName("StreamChanges")
                .WithSummary("Stream changes")
                .WithDescription("A server-sent event stream that names each area of the knowledge base as it changes.");

            return endpoints;
        }
    }

    private static Microsoft.AspNetCore.Http.IResult Handle(
        ChangeNotifier notifier,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        TypedResults.ServerSentEvents(Stream(notifier, timeProvider, cancellationToken));

    /// <summary>
    /// Proxies such as Cloudflare close connections that stay idle for around 100 seconds, so a ping is sent whenever
    /// nothing has changed for a while.
    /// </summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    internal static async IAsyncEnumerable<SseItem<ChangeEvent>> Stream(
        ChangeNotifier notifier,
        TimeProvider timeProvider,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The subscription has its own token so it can be stopped before it is disposed, whyever the stream ends.
        using CancellationTokenSource subscription = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IAsyncEnumerator<ChangeEvent> changes = notifier
            .SubscribeAsync(subscription.Token)
            .GetAsyncEnumerator(subscription.Token);
        Task<bool> nextChange = changes.MoveNextAsync().AsTask();
        try
        {
            // Sent straight away so the response starts and the browser knows the stream is open.
            yield return new SseItem<ChangeEvent>(new ChangeEvent("ping", timeProvider.GetUtcNow()), "ping");

            while (!cancellationToken.IsCancellationRequested)
            {
                Task completed = await Task.WhenAny(nextChange, Task.Delay(HeartbeatInterval, timeProvider, cancellationToken));
                if (completed != nextChange)
                {
                    yield return new SseItem<ChangeEvent>(new ChangeEvent("ping", timeProvider.GetUtcNow()), "ping");
                    continue;
                }

                if (!await nextChange)
                {
                    yield break;
                }

                yield return new SseItem<ChangeEvent>(changes.Current, "change");
                nextChange = changes.MoveNextAsync().AsTask();
            }
        }
        finally
        {
            // An async iterator throws NotSupportedException when disposed while a MoveNextAsync is still pending, as
            // one is whenever the client disconnects between changes, so the pending read is cancelled and awaited first.
            await subscription.CancelAsync();
            await StopAsync(nextChange);
            await changes.DisposeAsync();
        }
    }

    private static async Task StopAsync(Task<bool> nextChange)
    {
        try
        {
            await nextChange;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
