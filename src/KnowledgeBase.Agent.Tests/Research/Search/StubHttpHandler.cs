using System.Net;
using System.Text;

namespace KnowledgeBase.Agent.Tests.Research.Search;

/// <summary>Answers every request with <see cref="Respond"/> and records the requests, so a source never reaches the network.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

    public List<HttpRequestMessage> Requests { get; } = [];

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>A client factory whose clients all send through <paramref name="handler"/>.</summary>
    public static IHttpClientFactory FactoryFor(StubHttpHandler handler)
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        return factory;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Respond(request));
    }
}
