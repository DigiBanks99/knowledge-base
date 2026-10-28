using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research.Models;

public sealed class BlockedDomainsTests
{
    private readonly BlockedDomains _blocked = new(["contentfarm.example", "WWW.Spam.Example", "  tracker.example.  ", "bücher.example"]);

    [Test]
    [Arguments("https://bücher.example/post")]
    [Arguments("https://xn--bcher-kva.example/post")]
    [Arguments("https://contentfarm.example/post")]
    [Arguments("https://www.contentfarm.example/post")]
    [Arguments("https://blog.contentfarm.example/post")]
    [Arguments("http://CONTENTFARM.example/post")]
    [Arguments("https://spam.example/post")]
    [Arguments("https://tracker.example/post")]
    public void AResultIsOnABlockedDomain(string url)
    {
        bool blocked = _blocked.Blocks(url);

        ItShouldBeBlocked();

        void ItShouldBeBlocked() => blocked.ShouldBeTrue();
    }

    [Test]
    [Arguments("https://example.com/post")]
    [Arguments("https://notcontentfarm.example/post")]
    [Arguments("https://contentfarm.example.com/post")]
    [Arguments("not a url")]
    public void AResultIsOnAnotherDomain(string url)
    {
        bool blocked = _blocked.Blocks(url);

        ItShouldNotBeBlocked();

        void ItShouldNotBeBlocked() => blocked.ShouldBeFalse();
    }

    [Test]
    public void NothingIsBlocked()
    {
        bool blocked = BlockedDomains.None.Blocks("https://contentfarm.example/post");

        ItShouldNotBeBlocked();

        void ItShouldNotBeBlocked() => blocked.ShouldBeFalse();
    }
}
