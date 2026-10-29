using KnowledgeBase.Agent.Agents.Models;
using KnowledgeBase.Agent.Agents.Queries;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Agents.Endpoints;

internal static class ListAgentProviders
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapListAgentProviders()
        {
            endpoints
                .MapGet("providers", Handle)
                .WithName(nameof(ListAgentProviders))
                .WithSummary("List the agents' providers")
                .WithDescription("Returns each agent's provider, the deployment's default and the providers it can switch to, with the model each runs.")
                .Produces<IReadOnlyList<AgentProviderSetting>>()
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static IResult Handle(ListAgentProvidersQueryHandler handler) => handler
        .Handle(new ListAgentProvidersQuery())
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: StatusCodes.Status500InternalServerError));
}
