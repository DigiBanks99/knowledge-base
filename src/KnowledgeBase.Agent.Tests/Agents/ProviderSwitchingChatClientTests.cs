using KnowledgeBase.Agent.Agents;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Agents;

public sealed class ProviderSwitchingChatClientTests : IDisposable
{
    private readonly IOptionsMonitor<AgentModelOptions> _options = Substitute.For<IOptionsMonitor<AgentModelOptions>>();
    private readonly Dictionary<ModelProvider, IChatClient> _providers = new()
    {
        [ModelProvider.Anthropic] = Substitute.For<IChatClient>(),
        [ModelProvider.OpenAICompatible] = Substitute.For<IChatClient>()
    };
    private readonly List<ModelProvider> _created = [];
    private readonly ProviderSwitchingChatClient _client;

    public ProviderSwitchingChatClientTests()
    {
        foreach ((ModelProvider provider, IChatClient client) in _providers)
        {
            client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
                .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, provider.ToString())));
        }

        OnProvider(ModelProvider.Anthropic);
        _client = new ProviderSwitchingChatClient(KnownAgents.Librarian, _options, provider =>
        {
            _created.Add(provider);
            return _providers[provider];
        });
    }

    public void Dispose() => _client.Dispose();

    [Test]
    public async Task TheAgentIsSwitchedBetweenRequests()
    {
        ChatResponse before = await _client.GetResponseAsync("Who is Jane?");
        OnProvider(ModelProvider.OpenAICompatible);
        ChatResponse after = await _client.GetResponseAsync("Who is Jane?");

        ItShouldSendEachRequestToTheProviderOfTheMoment();

        void ItShouldSendEachRequestToTheProviderOfTheMoment() =>
            new[] { before.Text, after.Text }.ShouldBe(["Anthropic", "OpenAICompatible"]);
    }

    [Test]
    public async Task TheAgentIsSwitchedBack()
    {
        await _client.GetResponseAsync("Who is Jane?");
        OnProvider(ModelProvider.OpenAICompatible);
        await _client.GetResponseAsync("Who is Jane?");
        OnProvider(ModelProvider.Anthropic);
        await _client.GetResponseAsync("Who is Jane?");

        ItShouldCreateEachProvidersClientOnce();

        void ItShouldCreateEachProvidersClientOnce() =>
            _created.ShouldBe([ModelProvider.Anthropic, ModelProvider.OpenAICompatible]);
    }

    [Test]
    public async Task TheAppStopsAfterTheAgentUsedOneProvider()
    {
        await _client.GetResponseAsync("Who is Jane?");
        _client.Dispose();

        ItShouldNeverCreateTheOtherProvidersClient();
        ItShouldDisposeTheClientItCreated();

        void ItShouldNeverCreateTheOtherProvidersClient() => _created.ShouldBe([ModelProvider.Anthropic]);

        void ItShouldDisposeTheClientItCreated() => _providers[ModelProvider.Anthropic].Received().Dispose();
    }

    private void OnProvider(ModelProvider provider) =>
        _options.Get(KnownAgents.Librarian).Returns(new AgentModelOptions { Provider = provider, Instructions = "You are the Librarian." });
}
