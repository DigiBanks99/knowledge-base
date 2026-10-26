using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research.Models;

public sealed class TopicPathsTests
{
    [Test]
    [Arguments("projects/foo/index.md", "projects/foo")]
    [Arguments("projects/foo/notes/design.md", "projects/foo")]
    [Arguments("projects/foo/", "projects/foo")]
    [Arguments("projects/foo", "projects/foo")]
    [Arguments("learning/bar.md", "learning/bar.md")]
    [Arguments("plans/2026-q4.md", "plans/2026-q4.md")]
    [Arguments("./projects/foo/a.md", "projects/foo")]
    [Arguments("/projects/foo/a.md", "projects/foo")]
    [Arguments(@"projects\foo\a.md", "projects/foo")]
    [Arguments("Projects/Foo/a.md", "projects/Foo")]
    [Arguments("projects/v1.2-migration", "projects/v1.2-migration")]
    [Arguments("projects/v1.2-migration/index.md", "projects/v1.2-migration")]
    [Arguments("projects/diagram.png", "projects/diagram.png")]
    public void APathInAResearchedSectionChanges(string wikiPath, string topic)
    {
        Maybe<string> mapped = TopicPaths.TopicOf(wikiPath);

        ItShouldBelongToItsTopEntry();

        void ItShouldBelongToItsTopEntry() => mapped.ShouldBe(Maybe.From(topic));
    }

    [Test]
    [Arguments("projects/index.md")]
    [Arguments("projects")]
    [Arguments("people/jane-doe/index.md")]
    [Arguments("decisions/use-git.md")]
    [Arguments("index.md")]
    [Arguments("projects/../people/jane.md")]
    [Arguments("projects/.git/config")]
    [Arguments("")]
    public void APathIsNotATopic(string wikiPath)
    {
        Maybe<string> mapped = TopicPaths.TopicOf(wikiPath);

        ItShouldBeIgnored();

        void ItShouldBeIgnored() => mapped.HasNoValue.ShouldBeTrue();
    }

    [Test]
    [Arguments("projects/foo", "projects--foo")]
    [Arguments("learning/bar.md", "learning--bar.md")]
    [Arguments("projects/my--thing", "projects--my--thing")]
    public void ATopicIsNamedForAFile(string topic, string slug)
    {
        string named = TopicPaths.Slug(topic);
        Maybe<string> resolved = TopicPaths.FromSlug(named);

        ItShouldReplaceSlashes();
        ItShouldResolveBackToTheTopic();

        void ItShouldReplaceSlashes() => named.ShouldBe(slug);

        void ItShouldResolveBackToTheTopic() => resolved.ShouldBe(Maybe.From(topic));
    }

    [Test]
    [Arguments("people--jane-doe")]
    [Arguments("projects--..")]
    [Arguments("..--projects--foo")]
    [Arguments("projects--foo/bar.md")]
    [Arguments("projects--index.md")]
    [Arguments("projects")]
    [Arguments("--foo")]
    public void ASlugDoesNotNameATopic(string slug)
    {
        Maybe<string> resolved = TopicPaths.FromSlug(slug);

        ItShouldBeRefused();

        void ItShouldBeRefused() => resolved.HasNoValue.ShouldBeTrue();
    }
}
