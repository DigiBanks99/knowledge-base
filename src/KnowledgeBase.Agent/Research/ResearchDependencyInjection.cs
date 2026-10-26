using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Queries;
using KnowledgeBase.Agent.Runs;

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
                .AddSingleton<ListDenyListQueryHandler>()
                .AddSingleton<IWikiTopics, WikiTopics>()
                .AddSingleton<RecordTopicChangesCommandHandler>()
                .AddSingleton<DiscoverTopicsCommandHandler>()
                .AddSingleton<SetTopicMutedCommandHandler>()
                .AddSingleton<ListTopicsQueryHandler>()
                .AddSingleton<IAgentRunObserver, TopicDiscoveryObserver>();

            return builder;
        }
    }
}
