using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Queries;

namespace KnowledgeBase.Agent.Research;

internal static class ResearchDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddResearch()
        {
            builder.Services
                .AddSingleton<IResearchStore, ResearchStore>()
                .AddSingleton<RecordFactSubjectsCommandHandler>()
                .AddSingleton<SeedDenyListFromPeopleCommandHandler>()
                .AddSingleton<SetDenyListEntryStateCommandHandler>()
                .AddSingleton<ListDenyListQueryHandler>();

            return builder;
        }
    }
}