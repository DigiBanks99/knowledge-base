using KnowledgeBase.Agent.Research.Fetch;

namespace KnowledgeBase.Agent.Tests.Research.Fetch;

public sealed class RobotsRulesTests
{
    private const string Token = "KnowledgeBase-Research";

    [Test]
    [Arguments("/blog/post", true)]
    [Arguments("/private/notes", false)]
    [Arguments("/private/public/page", true)]
    [Arguments("/search?q=atlas", false)]
    [Arguments("/files/report.pdf", false)]
    [Arguments("/files/report.pdf?download=1", true)]
    [Arguments("/robots.txt", true)]
    public void ASiteSetsRulesForEveryone(string path, bool expected)
    {
        RobotsRules rules = RobotsRules.Parse(
            """
            # Everyone
            User-agent: *
            Disallow: /private/
            Allow: /private/public/
            Disallow: /search
            Disallow: /*.pdf$
            """,
            Token);

        bool allowed = rules.IsAllowed(path);

        ItShouldApplyTheLongestMatchingRule();

        void ItShouldApplyTheLongestMatchingRule() => allowed.ShouldBe(expected);
    }

    [Test]
    [Arguments("/blog/post", false)]
    [Arguments("/about", true)]
    public void ASiteSetsRulesForTheFetcher(string path, bool expected)
    {
        RobotsRules rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow: /

            User-agent: SomeBot
            User-agent: knowledgebase-research
            Disallow: /blog
            """,
            Token);

        bool allowed = rules.IsAllowed(path);

        ItShouldFollowOnlyTheFetchersGroup();

        void ItShouldFollowOnlyTheFetchersGroup() => allowed.ShouldBe(expected);
    }

    [Test]
    public void ASiteDisallowsOnlyOtherCrawlers()
    {
        RobotsRules rules = RobotsRules.Parse(
            """
            User-agent: GPTBot
            Disallow: /
            """,
            Token);

        bool allowed = rules.IsAllowed("/blog/post");

        ItShouldAllowThePage();

        void ItShouldAllowThePage() => allowed.ShouldBeTrue();
    }

    [Test]
    public void ASiteDisallowsNothing()
    {
        RobotsRules rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow:
            """,
            Token);

        bool allowed = rules.IsAllowed("/blog/post");

        ItShouldAllowThePage();

        void ItShouldAllowThePage() => allowed.ShouldBeTrue();
    }

    [Test]
    public void ARuleAllowsAndDisallowsTheSamePath()
    {
        RobotsRules rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow: /page
            Allow: /page
            """,
            Token);

        bool allowed = rules.IsAllowed("/page");

        ItShouldAllowThePage();

        void ItShouldAllowThePage() => allowed.ShouldBeTrue();
    }
}
