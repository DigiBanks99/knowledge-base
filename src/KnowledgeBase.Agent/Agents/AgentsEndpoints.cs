using KnowledgeBase.Agent.Agents.Endpoints;

namespace KnowledgeBase.Agent.Agents;

internal static class AgentsEndpoints
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapAgentsEndpoints()
        {
            endpoints.MapGroup("agents")
                .WithTags("agents")
                .MapListAgentProviders()
                .MapSetAgentProvider();

            return endpoints;
        }
    }
}
