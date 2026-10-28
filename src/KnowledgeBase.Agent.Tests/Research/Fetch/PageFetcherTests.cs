using System.Net;
using System.Text;

using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Curation;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Fetch;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Tests.Research.Search;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Fetch;

public sealed class PageFetcherTests
{
    private const string Article = "https://example.com/blog/atlas-2";
    private const int MaxBytes = 64 * 1024;
    private const int TokenBudget = 400;

    private static readonly IPAddress Public = IPAddress.Parse("93.184.215.14");

    private readonly Dictionary<string, Func<HttpResponseMessage>> _pages = new(StringComparer.Ordinal);
    private readonly StubHttpHandler _handler = new();
    private readonly IHostResolver _resolver = Substitute.For<IHostResolver>();
    private readonly PageFetcher _fetcher;

    public PageFetcherTests()
    {
        _handler.Respond = request => _pages.TryGetValue(request.RequestUri!.ToString(), out Func<HttpResponseMessage>? page)
            ? page()
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        _resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([Public]);
        _resolver.ResolveAsync("internal.example.com", Arg.Any<CancellationToken>()).Returns([IPAddress.Parse("10.0.0.5")]);

        // One token a word, so budgets are easy to reason about.
        IPromptTokenCounter counter = Substitute.For<IPromptTokenCounter>();
        counter.CountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<int, Error>(call.Arg<string>().Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Length));

        IOptions<ResearchSettings> settings = Options.Create(new ResearchSettings
        {
            FetchMaxBytes = MaxBytes,
            FetchTimeout = TimeSpan.FromMilliseconds(250),
            FetchPolitenessDelay = TimeSpan.Zero,
            FetchMaxRedirects = 2,
            TriageContentTokenBudget = TokenBudget
        });
        _fetcher = new PageFetcher(
            StubHttpHandler.FactoryFor(_handler),
            new FetchTarget(_resolver),
            new PolitenessGate(settings, TimeProvider.System),
            new ReadabilityExtractor(),
            new ContentBudget(counter),
            settings,
            TimeProvider.System);
    }

    [Test]
    public async Task AnArticleIsFetched()
    {
        _pages[Article] = () => Html(ArticlePage(paragraphs: 6));

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldCheckRobotsTxtFirst();
        ItShouldIdentifyItself();
        ItShouldKeepTheArticleAsMarkdown();
        ItShouldLeaveOutThePageChrome();

        void ItShouldCheckRobotsTxtFirst() => _handler.Requests.Select(request => request.RequestUri!.ToString())
            .ShouldBe(["https://example.com/robots.txt", Article]);

        void ItShouldIdentifyItself() => _handler.Requests.ShouldAllBe(request =>
            request.Headers.UserAgent.ToString() == PageFetcher.UserAgent);

        void ItShouldKeepTheArticleAsMarkdown() => fetched.Value.ShouldSatisfyAllConditions(
            content => content.Text.ShouldContain("## Migrating from Atlas 1"),
            content => content.Text.ShouldContain("Paragraph 6 of the release notes"),
            content => content.Text.ShouldContain("[the migration guide](https://example.com/guide)"),
            content => content.Truncated.ShouldBeFalse());

        void ItShouldLeaveOutThePageChrome() => fetched.Value.Text.ShouldSatisfyAllConditions(
            text => text.ShouldNotContain("Subscribe to our newsletter"),
            text => text.ShouldNotContain("Copyright Example Corp"),
            text => text.ShouldNotContain("trackVisitor"));
    }

    [Test]
    public async Task AnArticleIsLongerThanTheBudget()
    {
        _pages[Article] = () => Html(ArticlePage(paragraphs: 80));

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldCutItToTheBudget();

        void ItShouldCutItToTheBudget() => fetched.Value.ShouldSatisfyAllConditions(
            content => content.Text.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Length.ShouldBeLessThanOrEqualTo(TokenBudget),
            content => content.Truncated.ShouldBeTrue());
    }

    [Test]
    public async Task APageIsPlainText()
    {
        _pages[Article] = () => Content("Atlas 2 is out.\n", "text/plain");

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldKeepTheTextAsItIs();

        void ItShouldKeepTheTextAsItIs() => fetched.Value.Text.ShouldBe("Atlas 2 is out.");
    }

    [Test]
    [Arguments("https://internal.example.com/admin")]
    [Arguments("http://127.0.0.1/admin")]
    [Arguments("http://[::1]/admin")]
    [Arguments("http://169.254.169.254/latest/meta-data/")]
    [Arguments("http://[::ffff:10.0.0.1]/")]
    [Arguments("https://example.com:8443/blog")]
    [Arguments("ftp://example.com/blog")]
    [Arguments("file:///etc/passwd")]
    [Arguments("https://user:secret@example.com/blog")]
    [Arguments("not a url")]
    public async Task AResultPointsSomewhereTheFetcherMayNotGo(string url)
    {
        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(url, BlockedDomains.None, CancellationToken.None);

        ItShouldBeRefused();
        ItShouldSendNoRequest();

        void ItShouldBeRefused() => fetched.Error.ShouldBeOfType<PageFetchRefusedError>();

        void ItShouldSendNoRequest() => _handler.Requests.ShouldBeEmpty();
    }

    [Test]
    [Arguments("http://169.254.169.254/latest/meta-data/")]
    [Arguments("http://localhost.example.com:8080/")]
    [Arguments("https://internal.example.com/admin")]
    [Arguments("http://10.0.0.5/")]
    public async Task APageRedirectsSomewhereTheFetcherMayNotGo(string location)
    {
        _resolver.ResolveAsync("localhost.example.com", Arg.Any<CancellationToken>()).Returns([IPAddress.Loopback]);
        _pages[Article] = () => Redirect(location);

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldBeRefused();
        ItShouldNeverRequestTheRedirectTarget();

        void ItShouldBeRefused() => fetched.Error.ShouldBeOfType<PageFetchRefusedError>().Message.ShouldContain(new Uri(location).Host);

        void ItShouldNeverRequestTheRedirectTarget() =>
            _handler.Requests.ShouldAllBe(request => request.RequestUri!.Host == "example.com");
    }

    [Test]
    public async Task APageRedirectsToItsNewAddress()
    {
        _pages[Article] = () => Redirect("/blog/atlas-2-released");
        _pages["https://example.com/blog/atlas-2-released"] = () => Content("Atlas 2 is out.", "text/plain");

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldFetchTheNewAddress();

        void ItShouldFetchTheNewAddress() => fetched.Value.Text.ShouldBe("Atlas 2 is out.");
    }

    [Test]
    public async Task APageRedirectsToABlockedDomain()
    {
        _pages[Article] = () => Redirect("https://www.content-farm.example/atlas");

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(
            Article,
            new BlockedDomains(["content-farm.example"]),
            CancellationToken.None);

        ItShouldBeRefused();
        ItShouldNeverRequestTheBlockedPage();

        void ItShouldBeRefused() => fetched.Error.ShouldBeOfType<PageFetchRefusedError>().Reason.ShouldContain("blocked");

        void ItShouldNeverRequestTheBlockedPage() =>
            _handler.Requests.ShouldAllBe(request => request.RequestUri!.Host == "example.com");
    }

    [Test]
    public async Task APageDeclaresItsCharsetOnlyInTheMarkup()
    {
        _pages[Article] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.Latin1.GetBytes(
                ArticlePage(paragraphs: 6).Replace("<head>", "<head><meta charset=\"windows-1252\">", StringComparison.Ordinal)
                    .Replace("Atlas 2 changes", "Café Atlas 2 changes", StringComparison.Ordinal)))
            {
                Headers = { ContentType = new("text/html") }
            }
        };

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldDecodeItWithThatCharset();

        void ItShouldDecodeItWithThatCharset() => fetched.Value.Text.ShouldContain("Café Atlas 2 changes");
    }

    [Test]
    public async Task AnArticleHasImages()
    {
        _pages[Article] = () => Html(ArticlePage(paragraphs: 6).Replace(
            "<h2>",
            "<p><img src=\"https://tracker.example/p.png?x=(1)\" alt=\"a [chart]\"></p><h2>",
            StringComparison.Ordinal));

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldLeaveThemOut();

        void ItShouldLeaveThemOut() => fetched.Value.Text.ShouldNotContain("tracker.example");
    }

    [Test]
    public async Task APageRedirectsInALoop()
    {
        _pages[Article] = () => Redirect("https://example.com/a");
        _pages["https://example.com/a"] = () => Redirect("https://example.com/b");
        _pages["https://example.com/b"] = () => Redirect(Article);

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldGiveUp();

        void ItShouldGiveUp() => fetched.Error.ShouldBeOfType<PageFetchFailedError>().Reason.ShouldContain("redirected more than 2 times");
    }

    [Test]
    public async Task ThePageIsLargerThanTheLimit()
    {
        _pages[Article] = () => Html(new string('x', MaxBytes + 1));

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldNotBeRead();

        void ItShouldNotBeRead() => fetched.Error.ShouldBeOfType<PageFetchFailedError>().Reason.ShouldContain("larger than 64 KiB");
    }

    [Test]
    public async Task ThePageIsLargerThanTheLimitButDoesNotSaySo()
    {
        _pages[Article] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnknownLengthStream(new byte[MaxBytes + 1]))
            {
                Headers = { ContentType = new("text/html") }
            }
        };

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldStopReadingAtTheLimit();

        void ItShouldStopReadingAtTheLimit() => fetched.Error.ShouldBeOfType<PageFetchFailedError>().Reason.ShouldContain("larger than 64 KiB");
    }

    [Test]
    [Arguments("application/pdf")]
    [Arguments("image/png")]
    [Arguments("application/octet-stream")]
    public async Task ThePageIsNotText(string contentType)
    {
        _pages[Article] = () => Content("%PDF-1.7", contentType);

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldNotBeRead();

        void ItShouldNotBeRead() => fetched.Error.ShouldBeOfType<PageFetchFailedError>().Reason.ShouldContain(contentType);
    }

    [Test]
    public async Task ThePageNeverAnswers()
    {
        _handler.RespondAsync = async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/robots.txt")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldGiveUpAfterTheTimeout();

        void ItShouldGiveUpAfterTheTimeout() => fetched.Error.ShouldBeOfType<PageFetchFailedError>().Reason.ShouldContain("no answer within");
    }

    [Test]
    public async Task ThePageIsGone()
    {
        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldSayWhatTheSiteAnswered();

        void ItShouldSayWhatTheSiteAnswered() => fetched.Error.ShouldBeOfType<PageFetchFailedError>().Reason.ShouldContain("404");
    }

    [Test]
    public async Task RobotsTxtDisallowsThePage()
    {
        _pages["https://example.com/robots.txt"] = () => Content("User-agent: *\nDisallow: /blog/", "text/plain");
        _pages[Article] = () => Html(ArticlePage(paragraphs: 6));

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);
        Result<FittedContent, Error> again = await _fetcher.FetchAsync("https://example.com/blog/other", BlockedDomains.None, CancellationToken.None);

        ItShouldBeRefused();
        ItShouldNotRequestThePage();
        ItShouldReadRobotsTxtOnce();

        void ItShouldBeRefused()
        {
            fetched.Error.ShouldBeOfType<PageFetchRefusedError>().Reason.ShouldContain("robots.txt");
            again.Error.ShouldBeOfType<PageFetchRefusedError>();
        }

        void ItShouldNotRequestThePage() => _handler.Requests.ShouldNotContain(request => request.RequestUri!.ToString() == Article);

        void ItShouldReadRobotsTxtOnce() => _handler.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task RobotsTxtCannotBeReached()
    {
        _pages["https://example.com/robots.txt"] = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        _pages[Article] = () => Html(ArticlePage(paragraphs: 6));

        Result<FittedContent, Error> fetched = await _fetcher.FetchAsync(Article, BlockedDomains.None, CancellationToken.None);

        ItShouldBeRefused();

        void ItShouldBeRefused() => fetched.Error.ShouldBeOfType<PageFetchRefusedError>();
    }

    private static string ArticlePage(int paragraphs) =>
        $$"""
        <!doctype html>
        <html>
        <head><title>Atlas 2 released</title><script>trackVisitor();</script></head>
        <body>
          <header><nav><a href="/">Home</a> <a href="/blog">Blog</a> <a href="/about">About</a></nav></header>
          <aside class="sidebar"><h3>Newsletter</h3><p>Subscribe to our newsletter for weekly updates.</p></aside>
          <main>
            <article>
              <h1>Atlas 2 released</h1>
              <h2>Migrating from Atlas 1</h2>
              <p>Atlas 2 changes how components are registered. Read <a href="https://example.com/guide">the migration guide</a> before upgrading, as several APIs were removed.</p>
              {{string.Join("\n", Enumerable.Range(1, paragraphs).Select(paragraph =>
                  $"<p>Paragraph {paragraph} of the release notes describes, at some length, what changed in the renderer, the router and the build tooling, and why the maintainers chose to break compatibility.</p>"))}}
            </article>
          </main>
          <footer><p>Copyright Example Corp. All rights reserved.</p></footer>
        </body>
        </html>
        """;

    private static HttpResponseMessage Html(string html) => Content(html, "text/html");

    private static HttpResponseMessage Content(string text, string contentType) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, contentType) };

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    /// <summary>A body whose length is only known by reading it, as with a chunked response.</summary>
    private sealed class UnknownLengthStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }
}
