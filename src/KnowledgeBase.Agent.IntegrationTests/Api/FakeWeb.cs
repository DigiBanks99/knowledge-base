using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

/// <summary>
/// Stands in for the internet the search sources reach: every request is recorded and answered with
/// <see cref="Respond"/>, by default an empty Hacker News search.
/// </summary>
internal sealed class FakeWeb
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Json("""{ "hits": [] }""");

    public ConcurrentQueue<Uri> Requests { get; } = new();

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>
    /// A handler per client the factory builds, since the factory disposes handlers as it rotates them while the fake
    /// web lives as long as the API.
    /// </summary>
    internal sealed class Handler(FakeWeb web) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            web.Requests.Enqueue(request.RequestUri!);
            return Task.FromResult(web.Respond(request));
        }
    }
}
