using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Agents.Models;
using KnowledgeBase.Agent.Agents.Queries;

using Microsoft.Extensions.Configuration;

namespace KnowledgeBase.Agent.Tests.Agents.Queries;

public sealed class ListAgentProvidersQueryHandlerTests : IDisposable
{
    private readonly IAgentProviderOverrides _overrides = Substitute.For<IAgentProviderOverrides>();
    private readonly ConfigurationManager _configuration = new();
    private readonly ListAgentProvidersQueryHandler _handler;

    public ListAgentProvidersQueryHandlerTests()
    {
        _overrides.Defaults.Returns(KnownAgents.All.ToDictionary(
            agent => agent,
            agent => agent == KnownAgents.Pleb ? ModelProvider.OpenAICompatible : ModelProvider.Anthropic));
        _overrides.Overrides.Returns(new Dictionary<string, ModelProvider> { [KnownAgents.Pleb] = ModelProvider.Anthropic });
        _handler = new ListAgentProvidersQueryHandler(_overrides, _configuration);
    }

    public void Dispose() => _configuration.Dispose();

    [Test]
    public void TheSettingsPageOpens()
    {
        IReadOnlyList<AgentProviderSetting> settings = _handler.Handle(new ListAgentProvidersQuery()).Value;
        AgentProviderSetting pleb = settings.Single(setting => setting.Agent == KnownAgents.Pleb);

        ItShouldListEveryAgent();
        ItShouldShowAChoiceOverItsDefault();
        ItShouldOfferEveryProviderWithItsModel();
        ItShouldSayWhyAProviderCannotBeChosen();

        void ItShouldListEveryAgent() => settings.Select(setting => setting.Agent).ShouldBe(KnownAgents.All);

        void ItShouldShowAChoiceOverItsDefault() => pleb.ShouldSatisfyAllConditions(
            setting => setting.Provider.ShouldBe(ModelProvider.Anthropic),
            setting => setting.DefaultProvider.ShouldBe(ModelProvider.OpenAICompatible));

        void ItShouldOfferEveryProviderWithItsModel() => pleb.Choices
            .Select(choice => (choice.Provider, choice.Model))
            .ShouldBe([(ModelProvider.Anthropic, "claude-haiku-5-5"), (ModelProvider.OpenAICompatible, "Qwen3.5-4B-MTP-GGUF:UD-Q4_K_XL")]);

        void ItShouldSayWhyAProviderCannotBeChosen() => pleb.Choices
            .Select(choice => choice.Unavailable)
            .ShouldBe([
                $"The Anthropic provider requires '{AnthropicOptions.SectionName}:ApiKey'.",
                "The OpenAICompatible provider requires 'OpenAICompatible:Endpoint'."
            ]);
    }
}
