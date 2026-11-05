using KnowledgeBase.Agent.Research;

using YamlDotNet.RepresentationModel;

namespace KnowledgeBase.Agent.Tests.Research;

public sealed class ResearchSettingsTests
{
    private readonly ResearchSettings _settings = new();
    private readonly ResearchSettings _configured = new() { SearxngEngines = ["braveapi", "mwmbl"] };

    [Test]
    public void SearxngSettingsKeepTheirEngines()
    {
        string[] keepOnly = KeepOnly(Path.Combine(AppContext.BaseDirectory, "searxng", "settings.yml"));

        ItShouldExpectTheEnginesSearxngKeeps();

        void ItShouldExpectTheEnginesSearxngKeeps() => _settings.ExpectedSearxngEngines.ShouldBe(keepOnly);
    }

    [Test]
    public void TheSearxngEnginesAreConfigured()
    {
        IReadOnlyList<string> expected = _configured.ExpectedSearxngEngines;

        ItShouldExpectOnlyThose();

        void ItShouldExpectOnlyThose() => expected.ShouldBe(["braveapi", "mwmbl"]);
    }

    /// <summary>The engines under <c>use_default_settings.engines.keep_only</c>.</summary>
    private static string[] KeepOnly(string path)
    {
        using StreamReader reader = new(path);
        YamlStream yaml = [];
        yaml.Load(reader);
        YamlMappingNode root = (YamlMappingNode)yaml.Documents[0].RootNode;
        YamlMappingNode defaults = (YamlMappingNode)root["use_default_settings"];
        YamlMappingNode engines = (YamlMappingNode)defaults["engines"];
        return [.. ((YamlSequenceNode)engines["keep_only"]).Select(engine => ((YamlScalarNode)engine).Value!)];
    }
}
