using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Agents.Commands;
using KnowledgeBase.Agent.Agents.Errors;
using KnowledgeBase.Agent.Agents.Models;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Agents.Commands;

public sealed class SetAgentProviderCommandHandlerTests : IDisposable
{
    private readonly IAgentProviderOverrides _overrides = Substitute.For<IAgentProviderOverrides>();
    private readonly ConfigurationManager _configuration = new();
    private readonly SetAgentProviderCommandHandler _handler;

    public SetAgentProviderCommandHandlerTests()
    {
        _configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{AnthropicOptions.SectionName}:ApiKey"] = "a-key",
            [$"{OpenAICompatibleOptions.SectionName}:Endpoint"] = "http://localhost:8080/v1",
            [$"{OpenAICompatibleOptions.SectionName}:Model"] = "qwen",
            [$"{AnthropicOptions.SectionName}:Model"] = "claude-haiku-5-5"
        });
        _overrides.Defaults.Returns(KnownAgents.All.ToDictionary(agent => agent, _ => ModelProvider.Anthropic));
        _overrides.Overrides.Returns(new Dictionary<string, ModelProvider>());
        _handler = new SetAgentProviderCommandHandler(NullLogger<SetAgentProviderCommandHandler>.Instance, _overrides, _configuration);
    }

    public void Dispose() => _configuration.Dispose();

    [Test]
    public void TheAuthorPutsAnAgentOnTheLocalModel()
    {
        _overrides
            .When(overrides => overrides.Set(KnownAgents.Librarian, ModelProvider.OpenAICompatible))
            .Do(_ => _overrides.Overrides.Returns(new Dictionary<string, ModelProvider> { [KnownAgents.Librarian] = ModelProvider.OpenAICompatible }));

        Result<AgentProviderSetting, Error> result = _handler.Handle(new SetAgentProviderCommand("librarian", ModelProvider.OpenAICompatible));

        ItShouldKeepTheChoice();
        ItShouldReturnTheAgentOnItsNewProvider();

        void ItShouldKeepTheChoice() => _overrides.Received(1).Set(KnownAgents.Librarian, ModelProvider.OpenAICompatible);

        void ItShouldReturnTheAgentOnItsNewProvider() => result.Value.ShouldSatisfyAllConditions(
            setting => setting.Agent.ShouldBe(KnownAgents.Librarian),
            setting => setting.Provider.ShouldBe(ModelProvider.OpenAICompatible),
            setting => setting.DefaultProvider.ShouldBe(ModelProvider.Anthropic),
            setting => setting.Choices.ShouldBe(
            [
                new ProviderChoice(ModelProvider.Anthropic, "claude-haiku-5-5", null),
                new ProviderChoice(ModelProvider.OpenAICompatible, "qwen", null)
            ]));
    }

    [Test]
    public void TheAuthorPutsAnAgentBackOnItsDefault()
    {
        _overrides.Overrides.Returns(new Dictionary<string, ModelProvider> { [KnownAgents.Librarian] = ModelProvider.OpenAICompatible });

        Result<AgentProviderSetting, Error> result = _handler.Handle(new SetAgentProviderCommand(KnownAgents.Librarian, ModelProvider.Anthropic));

        ItShouldSucceed();
        ItShouldClearTheChoice();

        void ItShouldSucceed() => result.IsSuccess.ShouldBeTrue();

        void ItShouldClearTheChoice() => _overrides.Received(1).Set(KnownAgents.Librarian, null);
    }

    [Test]
    public void TheAuthorChoosesAProviderWithoutAKey()
    {
        _configuration[$"{AnthropicOptions.SectionName}:ApiKey"] = null;

        Result<AgentProviderSetting, Error> result = _handler.Handle(new SetAgentProviderCommand(KnownAgents.Editor, ModelProvider.Anthropic));

        ItShouldBeRefused();
        ItShouldChangeNothing();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<ProviderUnavailableError>()
            .Message.ShouldContain($"'{AnthropicOptions.SectionName}:ApiKey'");

        void ItShouldChangeNothing() => _overrides.DidNotReceiveWithAnyArgs().Set(default!, default);
    }

    [Test]
    public void TheAuthorChoosesAProviderForAnUnknownAgent()
    {
        Result<AgentProviderSetting, Error> result = _handler.Handle(new SetAgentProviderCommand("Scribe", ModelProvider.Anthropic));

        ItShouldBeNotFound();
        ItShouldChangeNothing();

        void ItShouldBeNotFound() => result.Error.ShouldBeOfType<UnknownAgentError>();

        void ItShouldChangeNothing() => _overrides.DidNotReceiveWithAnyArgs().Set(default!, default);
    }

    [Test]
    public void TheAuthorChoosesAProviderThatDoesNotExist()
    {
        Result<AgentProviderSetting, Error> result = _handler.Handle(new SetAgentProviderCommand(KnownAgents.Librarian, (ModelProvider)42));

        ItShouldBeRefused();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("ProviderInvalid");
    }
}
