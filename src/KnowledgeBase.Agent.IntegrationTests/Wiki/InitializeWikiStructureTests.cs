using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.IntegrationTests.Wiki;

public sealed class InitializeWikiStructureTests : IAsyncDisposable
{
    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task AnEmptyRootIsInitialised()
    {
        await _wiki.InitialiseAsync();

        ItShouldCreateEveryConfiguredFolder();
        ItShouldSeedTheRootIndex();
        ItShouldSeedEverySectionIndex();

        void ItShouldCreateEveryConfiguredFolder()
        {
            // Every *Directory setting is a folder the pipeline relies on, including ones added later. The keys folder
            // belongs to ASP.NET Core data protection, which creates it itself.
            IEnumerable<string> directories = typeof(WikiConfigurationSettings)
                .GetProperties()
                .Where(property => property.Name.EndsWith("Directory", StringComparison.Ordinal)
                    && property.Name != nameof(WikiConfigurationSettings.KeysDirectory))
                .Select(property => (string)property.GetValue(_wiki.Settings)!);
            directories.ShouldAllBe(directory => Directory.Exists(directory));
        }

        void ItShouldSeedTheRootIndex() =>
            File.ReadAllText(Path.Combine(_wiki.Settings.WikiDirectory, "index.md")).ShouldBe(WikiSections.RootIndex());

        void ItShouldSeedEverySectionIndex()
        {
            foreach (WikiSection section in WikiSections.All)
            {
                File.ReadAllText(Path.Combine(_wiki.Settings.WikiDirectory, section.Folder, "index.md"))
                    .ShouldBe(WikiSections.SectionIndex(section));
            }
        }
    }

    [Test]
    public async Task AnIndexPageWasAlreadyEdited()
    {
        const string editedIndex = "# People\n\n- [Susan](susan.md)\n";
        string indexPath = _wiki.Write(Path.Combine(_wiki.Settings.WikiDirectory, "people"), "index.md", editedIndex);

        await _wiki.InitialiseAsync();

        ItShouldKeepTheEdits();

        void ItShouldKeepTheEdits() => File.ReadAllText(indexPath).ShouldBe(editedIndex);
    }

    [Test]
    public async Task TheWikiIsInitialisedAgain()
    {
        await _wiki.InitialiseAsync();

        Exception? exception = await Catch.ExceptionAsync(_wiki.InitialiseAsync);

        ItShouldSucceed();

        void ItShouldSucceed() => exception.ShouldBeNull();
    }
}
