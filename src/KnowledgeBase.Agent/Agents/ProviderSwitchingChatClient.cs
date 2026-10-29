using System.Collections.Concurrent;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Agents;

/// <summary>
/// An agent's chat client that sends each request to the provider the agent is on at that moment, so a provider chosen
/// on the settings page applies from the agent's next request without a restart. Each provider's client is created the
/// first time the agent uses it.
/// </summary>
internal sealed class ProviderSwitchingChatClient(
    string agentName,
    IOptionsMonitor<AgentModelOptions> agentOptions,
    Func<ModelProvider, IChatClient> createClient)
    : IChatClient
{
    private readonly ConcurrentDictionary<ModelProvider, Lazy<IChatClient>> _clients = new();

    private IChatClient Current => _clients
        .GetOrAdd(agentOptions.Get(agentName).Provider, provider => new Lazy<IChatClient>(() => createClient(provider)))
        .Value;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Current.GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Current.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : Current.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        foreach (Lazy<IChatClient> client in _clients.Values.Where(client => client.IsValueCreated))
        {
            client.Value.Dispose();
        }
    }
}
