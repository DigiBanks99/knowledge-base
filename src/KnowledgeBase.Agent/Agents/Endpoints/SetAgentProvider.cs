using KnowledgeBase.Agent.Agents.Commands;
using KnowledgeBase.Agent.Agents.Errors;
using KnowledgeBase.Agent.Agents.Models;

using Microsoft.AspNetCore.Mvc;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Agents.Endpoints;

internal static class SetAgentProvider
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapSetAgentProvider()
        {
            endpoints
                .MapPut("providers", Handle)
                .WithName(nameof(SetAgentProvider))
                .WithSummary("Switch an agent's provider")
                .WithDescription("Puts an agent on a provider from its next request, and keeps the choice across restarts. Choosing the deployment's default clears the choice.")
                .Produces<AgentProviderSetting>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status409Conflict)
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static IResult Handle(
        [FromBody] SetAgentProviderCommand request,
        SetAgentProviderCommandHandler handler) => handler
        .Handle(request)
        .Match(
            Results.Ok,
            error => Results.Problem(error.Message, statusCode: error switch
            {
                ValidationError => StatusCodes.Status400BadRequest,
                UnknownAgentError => StatusCodes.Status404NotFound,
                ProviderUnavailableError => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            }));
}
