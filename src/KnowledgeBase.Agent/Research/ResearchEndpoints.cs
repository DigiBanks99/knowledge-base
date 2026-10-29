using KnowledgeBase.Agent.Research.Endpoints;

namespace KnowledgeBase.Agent.Research;

internal static class ResearchEndpoints
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapResearchEndpoints()
        {
            endpoints.MapGroup("research")
                .WithTags("research")
                .MapListDenyList()
                .MapAddDenyListEntry()
                .MapSetDenyListEntryState()
                .MapListTopics()
                .MapSetTopicMuted()
                .MapResearchTopicNow()
                .MapGetTopicResults();

            return endpoints;
        }
    }
}
