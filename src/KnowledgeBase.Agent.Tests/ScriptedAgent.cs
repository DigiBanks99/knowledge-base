using System.Text.Json;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace KnowledgeBase.Agent.Tests;

/// <summary>
/// Stands in for a model-backed agent. A test scripts what a run does, usually calling the agent's real tools, so the
/// code around the agent runs for real without a model.
/// </summary>
internal sealed class ScriptedAgent(string name) : AIAgent
{
    private readonly List<string> _prompts = [];

    public override string Name => name;

    /// <summary>Every prompt the agent was run with, in order.</summary>
    public IReadOnlyList<string> Prompts => _prompts;

    /// <summary>What a run does with its prompt. By default it answers without calling any tools.</summary>
    public Func<string, CancellationToken, Task<string>> Behaviour { get; set; } =
        (_, _) => Task.FromResult("Nothing to do.");

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string prompt = string.Join('\n', messages.Select(message => message.Text));
        _prompts.Add(prompt);
        string answer = await Behaviour(prompt, cancellationToken);
        return new AgentResponse(new ChatMessage(ChatRole.Assistant, answer));
    }

    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Scripted agents are not streamed.");

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Scripted agents do not use sessions.");

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Scripted agents do not use sessions.");

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Scripted agents do not use sessions.");
}
