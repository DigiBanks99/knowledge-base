using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research.Models;

public sealed class ResultUrlsTests
{
    [Test]
    [Arguments("https://example.com/post", "https://example.com/post")]
    [Arguments("http://example.com/post", "https://example.com/post")]
    [Arguments("HTTPS://Example.COM/post", "https://example.com/post")]
    [Arguments("https://www.example.com/post", "https://example.com/post")]
    [Arguments("https://example.com:443/post", "https://example.com/post")]
    [Arguments("http://example.com:80/post", "https://example.com/post")]
    [Arguments("https://example.com/post/", "https://example.com/post")]
    [Arguments("https://example.com/", "https://example.com")]
    [Arguments("https://example.com/post#comments", "https://example.com/post")]
    [Arguments("https://example.com/post?utm_source=hn&utm_medium=social", "https://example.com/post")]
    [Arguments("https://example.com/post?fbclid=abc&gclid=def&ref=hn", "https://example.com/post")]
    [Arguments("https://example.com/post?b=2&a=1", "https://example.com/post?a=1&b=2")]
    [Arguments("https://example.com/post?id=7&utm_campaign=x", "https://example.com/post?id=7")]
    [Arguments("https://example.com:8443/post", "https://example.com:8443/post")]
    [Arguments("  https://example.com/post  ", "https://example.com/post")]
    public void AResultUrlIsNormalised(string url, string expected)
    {
        Maybe<string> normalised = ResultUrls.Normalise(url);

        ItShouldKeepOnlyWhatIdentifiesThePage();

        void ItShouldKeepOnlyWhatIdentifiesThePage() => normalised.ShouldBe(Maybe.From(expected));
    }

    [Test]
    [Arguments("https://example.com/Post")]
    [Arguments("https://example.com/post?ID=7")]
    public void AResultUrlDiffersOnlyInThePathOrQueryCase(string url)
    {
        Maybe<string> normalised = ResultUrls.Normalise(url);

        ItShouldKeepThatCase();

        void ItShouldKeepThatCase() => normalised.Value.ShouldEndWith(url["https://example.com".Length..]);
    }

    [Test]
    [Arguments("")]
    [Arguments("not a url")]
    [Arguments("/relative/post")]
    [Arguments("ftp://example.com/file")]
    [Arguments("javascript:alert(1)")]
    [Arguments("mailto:someone@example.com")]
    public void ASourceReturnsSomethingThatIsNotAWebPage(string url)
    {
        Maybe<string> normalised = ResultUrls.Normalise(url);

        ItShouldHaveNoUrl();

        void ItShouldHaveNoUrl() => normalised.HasNoValue.ShouldBeTrue();
    }
}
