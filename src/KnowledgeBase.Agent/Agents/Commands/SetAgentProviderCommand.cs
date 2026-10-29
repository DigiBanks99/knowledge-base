using KnowledgeBase.Agent.Agents.Errors;
using KnowledgeBase.Agent.Agents.Models;
using KnowledgeBase.Agent.Agents.Queries;

namespace KnowledgeBase.Agent.Agents.Commands;

/// <summary>The author's choice of provider for an agent, applied from the agent's next request.</summary>
public record SetAgentProviderCommand(string Agent, ModelProvider Provider);

internal sealed class SetAgentProviderCommandHandler(
    ILogger<SetAgentProviderCommandHandler> logger,
    IAgentProviderOverrides overrides,
    IConfiguration configuration)
{
    public Result<AgentProviderSetting, Error> Handle(SetAgentProviderCommand request) => Result
        .Success<string?, Error>(KnownAgents.All.FirstOrDefault(agent => string.Equals(agent, request.Agent, StringComparison.OrdinalIgnoreCase)))
        .Ensure(agent => agent is not null, new UnknownAgentError(request.Agent))
        .Map(agent => agent!)
        .Ensure(
            _ => Enum.IsDefined(request.Provider),
            new ValidationError("ProviderInvalid", $"'{request.Provider}' is not a model provider."))
        .Bind(agent => AgentProviders.Problem(configuration, request.Provider) is { } problem
            ? Result.Failure<string, Error>(new ProviderUnavailableError(agent, request.Provider, problem))
            : Result.Success<string, Error>(agent))
        // Choosing the default clears the choice, so a later change to the deployment's default applies again.
        .Tap(agent => overrides.Set(
            agent,
            request.Provider == overrides.Defaults.GetValueOrDefault(agent, ModelProvider.Anthropic) ? null : request.Provider))
        .Tap(agent => logger.AgentProviderSet(agent, request.Provider))
        .Map(agent => ListAgentProvidersQueryHandler.Describe(overrides, configuration, agent));
}
