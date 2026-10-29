using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.IntegrationTests.Agents;

public sealed class AgentProviderOverridesTests : IDisposable
{
    private static readonly IReadOnlyDictionary<string, ModelProvider> Defaults =
        KnownAgents.All.ToDictionary(agent => agent, _ => ModelProvider.Anthropic);

    private readonly TemporaryDirectory _root = new();
    private readonly string _path;
    private readonly ConfigurationManager _configuration = new();
    private AgentProviderOverrides _overrides;

    public AgentProviderOverridesTests()
    {
        _path = new WikiConfigurationSettings { RootDirectory = _root.Path }.AgentProvidersPath;
        _configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{KnownAgents.Librarian}:Provider"] = nameof(ModelProvider.Anthropic),
            [$"{KnownAgents.Librarian}:Instructions"] = "You are the Librarian."
        });
        _overrides = Start();
    }

    public void Dispose()
    {
        _configuration.Dispose();
        _root.Dispose();
    }

    [Test]
    public void TheAuthorSwitchesAnAgent()
    {
        using ServiceProvider services = new ServiceCollection()
            .Configure<AgentModelOptions>(KnownAgents.Librarian, _configuration.GetSection(KnownAgents.Librarian))
            .BuildServiceProvider();
        IOptionsMonitor<AgentModelOptions> options = services.GetRequiredService<IOptionsMonitor<AgentModelOptions>>();
        ModelProvider before = options.Get(KnownAgents.Librarian).Provider;

        _overrides.Set(KnownAgents.Librarian, ModelProvider.OpenAICompatible);

        ItShouldStartOnTheDefault();
        ItShouldSwitchTheAgentsOptionsAtOnce();
        ItShouldKeepTheChoiceInTheSettingsFolder();

        void ItShouldStartOnTheDefault() => before.ShouldBe(ModelProvider.Anthropic);

        void ItShouldSwitchTheAgentsOptionsAtOnce() =>
            options.Get(KnownAgents.Librarian).Provider.ShouldBe(ModelProvider.OpenAICompatible);

        void ItShouldKeepTheChoiceInTheSettingsFolder() => File.ReadAllText(_path).ShouldContain("\"Librarian\": \"OpenAICompatible\"");
    }

    [Test]
    public void TheAppRestartsAfterAnAgentWasSwitched()
    {
        _overrides.Set(KnownAgents.Librarian, ModelProvider.OpenAICompatible);

        AgentProviderOverrides restarted = Start();

        ItShouldStartTheAgentOnTheChosenProvider();

        void ItShouldStartTheAgentOnTheChosenProvider() => restarted.Overrides.ShouldBe(
            new Dictionary<string, ModelProvider> { [KnownAgents.Librarian] = ModelProvider.OpenAICompatible });
    }

    [Test]
    public void TheAuthorPutsAnAgentBackOnItsDefault()
    {
        _overrides.Set(KnownAgents.Librarian, ModelProvider.OpenAICompatible);

        _overrides.Set(KnownAgents.Librarian, null);

        ItShouldRunOnTheDeploymentsProviderAgain();
        ItShouldForgetTheChoice();

        void ItShouldRunOnTheDeploymentsProviderAgain() =>
            _configuration[$"{KnownAgents.Librarian}:Provider"].ShouldBe(nameof(ModelProvider.Anthropic));

        void ItShouldForgetTheChoice() => Start().Overrides.ShouldBeEmpty();
    }

    [Test]
    public void TheFileNamesAnAgentOrProviderThatNoLongerExists()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, """{ "Scribe": "Anthropic", "pleb": "Anthropic", "Editor": "Gemini" }""");

        AgentProviderOverrides restarted = Start();

        ItShouldKeepOnlyTheKnownChoices();

        void ItShouldKeepOnlyTheKnownChoices() => restarted.Overrides.ShouldBe(
            new Dictionary<string, ModelProvider> { [KnownAgents.Pleb] = ModelProvider.Anthropic });
    }

    [Test]
    public async Task TheFileIsNotJson()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, "Librarian = OpenAICompatible");

        Exception? exception = await Catch.ExceptionAsync(() => Task.FromResult(Start()));

        ItShouldRefuseToStartNamingTheFile();

        void ItShouldRefuseToStartNamingTheFile() =>
            exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldContain(_path);
    }

    private AgentProviderOverrides Start()
    {
        AgentProviderOverrides overrides = new(_path, Defaults);
        ((IConfigurationBuilder)_configuration).Add(overrides);
        _overrides = overrides;
        return overrides;
    }
}
