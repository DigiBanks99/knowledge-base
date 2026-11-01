using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Fetch;

/// <summary>Fetches a search result's page and reduces it to the main content triage reads.</summary>
internal interface IPageFetcher
{
    /// <summary>
    /// The page's main content as markdown, within the triage token budget, or why it could not be fetched.
    /// </summary>
    /// <param name="blockedDomains">Domains never fetched, checked again on every redirect.</param>
    Task<Result<FittedContent, Error>> FetchAsync(string url, BlockedDomains blockedDomains, CancellationToken cancellationToken);
}

/// <summary>
/// Fetches pages from the open web safely and politely: every request, redirects and robots.txt included, goes through
/// the <see cref="FetchTarget"/> guard, waits its turn at the <see cref="PolitenessGate"/> and is bounded in time and
/// size; robots.txt is respected and only HTML and plain text are read.
/// </summary>
internal sealed partial class PageFetcher(
    IHttpClientFactory httpClientFactory,
    FetchTarget target,
    PolitenessGate politeness,
    IContentExtractor extractor,
    ContentBudget budget,
    IOptions<ResearchSettings> settings,
    TimeProvider timeProvider) : IPageFetcher
{
    public const string HttpClientName = "PageFetcher";

    /// <summary>The token robots.txt groups name the fetcher by.</summary>
    public const string ProductToken = "KnowledgeBase-Research";

    public const string UserAgent = $"{ProductToken}/1.0 (+https://github.com/DigiBanks99/knowledge-base)";

    // RFC 9309 lets a crawler cache robots.txt for a day and ignore anything past 500 KiB.
    private static readonly TimeSpan RobotsLifetime = TimeSpan.FromDays(1);
    private const int RobotsMaxBytes = 500 * 1024;
    private const int RobotsPruneThreshold = 1000;

    // How far into an HTML page a <meta charset> is looked for, as browsers do.
    private const int CharsetSniffBytes = 1024;

    private static readonly HashSet<string> HtmlTypes = new(["text/html", "application/xhtml+xml"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TextTypes = new(["text/plain", "text/markdown"], StringComparer.OrdinalIgnoreCase);

    private readonly ResearchSettings _settings = settings.Value;
    private readonly ConcurrentDictionary<string, (RobotsRules Rules, DateTimeOffset Until)> _robots =
        new(StringComparer.OrdinalIgnoreCase);

    static PageFetcher() =>
        // Pages still come in windows-1252, Shift_JIS and the like, which .NET only decodes with this provider.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public async Task<Result<FittedContent, Error>> FetchAsync(
        string url,
        BlockedDomains blockedDomains,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return new PageFetchRefusedError(url, "it is not an absolute URL.");
        }

        try
        {
            Result<Answer, Error> answered = await GetAsync(uri, isPage: true, blockedDomains, cancellationToken);
            if (answered.IsFailure)
            {
                return answered.Error;
            }

            using Answer answer = answered.Value;
            Result<string, Error> markdown = await ReadMarkdownAsync(answer);
            return markdown.IsSuccess
                ? await budget.FitAsync(markdown.Value, _settings.TriageContentTokenBudget, cancellationToken)
                : markdown.Error;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UriFormatException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new PageFetchFailedError(url, ex is OperationCanceledException
                ? $"no answer within {_settings.FetchTimeout.TotalSeconds:0} seconds."
                : ex.Message);
        }
    }

    /// <summary>
    /// Requests <paramref name="uri"/>, following redirects itself so that every hop is checked by the guard (and,
    /// for a page, by the blocked domains and robots.txt) before it is requested. Returns the first answer that is not
    /// a redirect. Each request gets its own timeout, started only once its politeness wait is over.
    /// </summary>
    private async Task<Result<Answer, Error>> GetAsync(
        Uri uri,
        bool isPage,
        BlockedDomains blockedDomains,
        CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        Uri current = uri;
        for (int redirects = 0; ; redirects++)
        {
            UnitResult<Error> allowed = await target.CheckAsync(current, cancellationToken);
            if (allowed.IsFailure)
            {
                return allowed.Error;
            }

            if (isPage && blockedDomains.Blocks(current.ToString()))
            {
                return new PageFetchRefusedError(current.ToString(), "its domain is blocked.");
            }

            if (isPage && !(await RobotsAsync(current, cancellationToken)).IsAllowed(current.PathAndQuery))
            {
                return new PageFetchRefusedError(current.ToString(), "the site's robots.txt disallows it.");
            }

            await politeness.WaitAsync(current.IdnHost, cancellationToken);
            CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_settings.FetchTimeout);
            HttpResponseMessage response;
            try
            {
                HttpRequestMessage request = new(HttpMethod.Get, current);
                request.Headers.UserAgent.ParseAdd(UserAgent);
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            }
            catch
            {
                timeout.Dispose();
                throw;
            }

            if (!IsRedirect(response.StatusCode))
            {
                return new Answer(response, current, timeout);
            }

            Uri? location = response.Headers.Location;
            int status = (int)response.StatusCode;
            response.Dispose();
            timeout.Dispose();
            if (location is null)
            {
                return new PageFetchFailedError(current.ToString(), $"it answered {status} without a location.");
            }

            if (redirects >= _settings.FetchMaxRedirects)
            {
                return new PageFetchFailedError(uri.ToString(), $"it redirected more than {_settings.FetchMaxRedirects} times.");
            }

            current = location.IsAbsoluteUri ? location : new Uri(current, location);
        }
    }

    /// <summary>
    /// The site's robots.txt rules, cached for a day. Per RFC 9309, a missing file (4xx) allows everything and one
    /// that cannot be reached (5xx, a network failure, a refused redirect) disallows everything; the latter is not
    /// cached, so the next page of the site tries again.
    /// </summary>
    private async Task<RobotsRules> RobotsAsync(Uri page, CancellationToken cancellationToken)
    {
        string origin = page.GetLeftPart(UriPartial.Authority);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (_robots.TryGetValue(origin, out (RobotsRules Rules, DateTimeOffset Until) cached) && cached.Until > now)
        {
            return cached.Rules;
        }

        RobotsRules rules;
        try
        {
            Result<Answer, Error> answered = await GetAsync(new Uri($"{origin}/robots.txt"), isPage: false, BlockedDomains.None, cancellationToken);
            if (answered.IsFailure)
            {
                return RobotsRules.DisallowAll;
            }

            using Answer answer = answered.Value;
            if (answer.Response.IsSuccessStatusCode)
            {
                Result<string, Error> text = await ReadTextAsync(answer, RobotsMaxBytes, cutToSize: true, sniffCharset: false);
                rules = text.IsSuccess ? RobotsRules.Parse(text.Value, ProductToken) : RobotsRules.DisallowAll;
            }
            else if ((int)answer.Response.StatusCode is >= 400 and < 500)
            {
                rules = RobotsRules.AllowAll;
            }
            else
            {
                return RobotsRules.DisallowAll;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UriFormatException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return RobotsRules.DisallowAll;
        }

        if (_robots.Count > RobotsPruneThreshold)
        {
            foreach (string expired in _robots.Where(entry => entry.Value.Until <= now).Select(entry => entry.Key).ToList())
            {
                _robots.TryRemove(expired, out _);
            }
        }

        _robots[origin] = (rules, now + RobotsLifetime);
        return rules;
    }

    private async Task<Result<string, Error>> ReadMarkdownAsync(Answer answer)
    {
        HttpResponseMessage response = answer.Response;
        if (!response.IsSuccessStatusCode)
        {
            return new PageFetchFailedError(answer.Uri.ToString(), $"it answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        bool isHtml = mediaType is not null && HtmlTypes.Contains(mediaType);
        if (!isHtml && (mediaType is null || !TextTypes.Contains(mediaType)))
        {
            return new PageFetchFailedError(answer.Uri.ToString(), $"its content type {mediaType ?? "(none)"} is not HTML or plain text.");
        }

        Result<string, Error> text = await ReadTextAsync(answer, _settings.FetchMaxBytes, cutToSize: false, sniffCharset: isHtml);
        if (text.IsFailure)
        {
            return text;
        }

        string markdown = isHtml ? extractor.ToMarkdown(answer.Uri, text.Value) : text.Value.Trim();
        return string.IsNullOrWhiteSpace(markdown)
            ? new PageFetchFailedError(answer.Uri.ToString(), "it has no readable content.")
            : markdown;
    }

    /// <summary>
    /// The body as text, read no further than <paramref name="maxBytes"/>: a larger body fails, or with
    /// <paramref name="cutToSize"/> is cut there. The declared length is not trusted, as a server can lie about it.
    /// </summary>
    private static async Task<Result<string, Error>> ReadTextAsync(Answer answer, long maxBytes, bool cutToSize, bool sniffCharset)
    {
        HttpResponseMessage response = answer.Response;
        if (!cutToSize && response.Content.Headers.ContentLength > maxBytes)
        {
            return TooLarge(answer.Uri, maxBytes);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(answer.Timeout.Token);
        using MemoryStream body = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, answer.Timeout.Token)) > 0)
        {
            int room = (int)(maxBytes - body.Length);
            if (read > room)
            {
                if (!cutToSize)
                {
                    return TooLarge(answer.Uri, maxBytes);
                }

                body.Write(chunk, 0, room);
                break;
            }

            body.Write(chunk, 0, read);
        }

        byte[] bytes = body.GetBuffer();
        string? charset = response.Content.Headers.ContentType?.CharSet
            ?? (sniffCharset ? MetaCharset(bytes, (int)body.Length) : null);
        return Decoding(charset).GetString(bytes, 0, (int)body.Length);
    }

    /// <summary>The charset a page declares in a <c>&lt;meta&gt;</c> near its start, for one served without it.</summary>
    private static string? MetaCharset(byte[] bytes, int length)
    {
        Match match = MetaCharsetPattern().Match(Encoding.ASCII.GetString(bytes, 0, Math.Min(length, CharsetSniffBytes)));
        return match.Success ? match.Groups["charset"].Value : null;
    }

    private static PageFetchFailedError TooLarge(Uri uri, long maxBytes) =>
        new(uri.ToString(), $"it is larger than {maxBytes / 1024} KiB.");

    private static Encoding Decoding(string? charset)
    {
        try
        {
            return string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"', '\'', ' '));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    [GeneratedRegex("""<meta[^>]+charset\s*=\s*["']?(?<charset>[A-Za-z0-9_.:-]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCharsetPattern();

    /// <summary>A response that is not a redirect, with the request's timeout, which still bounds reading the body.</summary>
    /// <param name="Uri">The URL that gave the answer, after any redirects.</param>
    private sealed record Answer(HttpResponseMessage Response, Uri Uri, CancellationTokenSource Timeout) : IDisposable
    {
        public void Dispose()
        {
            Response.Dispose();
            Timeout.Dispose();
        }
    }
}
