using System.Net;
using System.Net.Sockets;

using KnowledgeBase.Agent.Research.Errors;

namespace KnowledgeBase.Agent.Research.Fetch;

/// <summary>Resolves a host name to its addresses; a seam so tests never touch DNS.</summary>
internal interface IHostResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

internal sealed class DnsHostResolver : IHostResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

/// <summary>
/// The SSRF guard: decides whether a URL may be requested. It is asked before every request, redirects included, and
/// again by the connection itself, so a host that resolves differently the second time is still refused.
/// </summary>
internal sealed class FetchTarget(IHostResolver resolver)
{
    /// <summary>
    /// Whether <paramref name="uri"/> may be requested: http or https on the default port, with no credentials, and
    /// a host whose every address is public.
    /// </summary>
    public async Task<UnitResult<Error>> CheckAsync(Uri uri, CancellationToken cancellationToken)
    {
        string url = uri.ToString();
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new PageFetchRefusedError(url, "only http and https pages are fetched.");
        }

        if (!uri.IsDefaultPort)
        {
            return new PageFetchRefusedError(url, $"port {uri.Port} is not allowed; only 80 and 443 are.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return new PageFetchRefusedError(url, "URLs with credentials are not fetched.");
        }

        Result<IPAddress[], string> addresses = await ResolvePublicAsync(uri.IdnHost, cancellationToken);
        return addresses.IsSuccess
            ? UnitResult.Success<Error>()
            : new PageFetchRefusedError(url, addresses.Error);
    }

    /// <summary>
    /// The addresses of <paramref name="host"/>, or why it may not be connected to. Every address must be public, as
    /// the connection may use any of them.
    /// </summary>
    public async Task<Result<IPAddress[], string>> ResolvePublicAsync(string host, CancellationToken cancellationToken)
    {
        string bare = host.Trim('[', ']');
        IPAddress[] addresses;
        if (IPAddress.TryParse(bare, out IPAddress? literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await resolver.ResolveAsync(bare, cancellationToken);
            }
            // ArgumentException is a name DNS cannot even look up, such as one over 255 characters.
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                return $"{bare} could not be resolved: {ex.Message}";
            }
        }

        if (addresses.Length == 0)
        {
            return $"{bare} has no addresses.";
        }

        IPAddress? refused = addresses.FirstOrDefault(address => !AddressClassifier.IsPublic(address));
        return refused is null
            ? addresses
            : $"{bare} resolves to {refused}, which is not a public address.";
    }

    /// <summary>
    /// The handler every fetch is sent through. It connects only to an address it has vetted itself, so a DNS answer
    /// that changes between the check and the connection (DNS rebinding) cannot reach a private address; redirects
    /// are left to the fetcher, which checks each one.
    /// </summary>
    public SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = async (context, cancellationToken) =>
        {
            Result<IPAddress[], string> addresses = await ResolvePublicAsync(context.DnsEndPoint.Host, cancellationToken);
            if (addresses.IsFailure)
            {
                throw new HttpRequestException($"Refused to connect: {addresses.Error}");
            }

            Socket socket = new(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses.Value, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };
}
