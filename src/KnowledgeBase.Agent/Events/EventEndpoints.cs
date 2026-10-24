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

    private static Microsoft.AspNetCore.Http.IResult Handle(ChangeNotifier notifier, CancellationToken cancellationToken) =>
        TypedResults.ServerSentEvents(Stream(notifier, cancellationToken));

    /// <summary>
    /// Proxies such as Cloudflare close connections that stay idle for around 100 seconds, so a ping is sent whenever
    /// nothing has changed for a while.
    /// </summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private static async IAsyncEnumerable<SseItem<ChangeEvent>> Stream(
        ChangeNotifier notifier,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using IAsyncEnumerator<ChangeEvent> changes = notifier
            .SubscribeAsync(cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        Task<bool> nextChange = changes.MoveNextAsync().AsTask();

        // Sent straight away so the response starts and the browser knows the stream is open.
        yield return new SseItem<ChangeEvent>(new ChangeEvent("ping", DateTimeOffset.UtcNow), "ping");

        while (!cancellationToken.IsCancellationRequested)
        {
            Task completed = await Task.WhenAny(nextChange, Task.Delay(HeartbeatInterval, cancellationToken));
            if (completed != nextChange)
            {
                yield return new SseItem<ChangeEvent>(new ChangeEvent("ping", DateTimeOffset.UtcNow), "ping");
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
}