using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Wiki;

/// <summary>
/// The root is never created: resolving paths is string work, so these tests stay off the disk.
/// </summary>
public sealed class WikiPathsTests
{
    private readonly string _wikiRoot;
    private readonly WikiPaths _paths;

    public WikiPathsTests()
    {
        IOptions<WikiConfigurationSettings> options = Substitute.For<IOptions<WikiConfigurationSettings>>();
        options.Value.Returns(new WikiConfigurationSettings
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), $"knowledge-base-{Guid.NewGuid():N}")
        });
        _wikiRoot = Path.GetFullPath(options.Value.WikiDirectory);
        _paths = new WikiPaths(options);
    }

    [Test]
    public void APagePathIsResolved()
    {
        string fullPath = _paths.Resolve("people/susan.md");

        ItShouldPointInsideTheWiki();

        void ItShouldPointInsideTheWiki() => fullPath.ShouldBe(Path.Combine(_wikiRoot, "people", "susan.md"));
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments(".")]
    public void TheWikiRootIsResolved(string path)
    {
        bool resolved = _paths.TryResolve(path, out string fullPath);

        ItShouldBeAllowed();
        ItShouldPointAtTheRoot();

        void ItShouldBeAllowed() => resolved.ShouldBeTrue();

        void ItShouldPointAtTheRoot() => fullPath.ShouldBe(_wikiRoot);
    }

    [Test]
    public void APathStepsInAndBackOut()
    {
        bool resolved = _paths.TryResolve("people/../projects/index.md", out string fullPath);

        ItShouldBeAllowed();
        ItShouldNormaliseThePath();

        void ItShouldBeAllowed() => resolved.ShouldBeTrue();

        void ItShouldNormaliseThePath() => fullPath.ShouldBe(Path.Combine(_wikiRoot, "projects", "index.md"));
    }

    [Test]
    [Arguments("../notes/new/note.md")]
    [Arguments("people/../../facts/collection.json")]
    [Arguments("../wiki-backup/index.md")]
    public void APathLeavesTheWiki(string path)
    {
        bool resolved = _paths.TryResolve(path, out _);
        Exception? exception = Catch.Exception(() => _paths.Resolve(path));

        ItShouldBeRefused();
        ItShouldExplainTheRefusal();

        void ItShouldBeRefused() => resolved.ShouldBeFalse();

        void ItShouldExplainTheRefusal() => exception
            .ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldBe($"The path '{path}' is outside the wiki. All paths must be relative to the wiki root.");
    }

    [Test]
    public void AnAbsolutePathOutsideTheWikiIsGiven()
    {
        string outside = Path.Combine(Path.GetTempPath(), "elsewhere", "page.md");

        bool resolved = _paths.TryResolve(outside, out _);

        ItShouldBeRefused();

        void ItShouldBeRefused() => resolved.ShouldBeFalse();
    }

    [Test]
    public void AFullPagePathIsMadeRelative()
    {
        string relativePath = _paths.ToRelative(Path.Combine(_wikiRoot, "people", "susan.md"));

        ItShouldUseForwardSlashes();

        void ItShouldUseForwardSlashes() => relativePath.ShouldBe("people/susan.md");
    }
}