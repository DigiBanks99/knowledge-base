using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Curation;
using KnowledgeBase.Agent.Research.Jobs;
using KnowledgeBase.Agent.Research.Queries;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Status;

namespace KnowledgeBase.Agent.Research;

internal static class ResearchDependencyInjection
{
    private static readonly TimeSpan TokenizeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(20);

    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddResearch()
        {
            builder.Services.Configure<ResearchSettings>(builder.Configuration.GetSection(ResearchSettings.SectionName));

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
                .AddSingleton<IAgentRunObserver, TopicDiscoveryObserver>()
                .AddSingleton<SetTopicQueriesCommandHandler>()
                .AddSingleton<KeepTopicQueriesCommandHandler>()
                .AddSingleton<CurateTopicsCommandHandler>()
                .AddSingleton<PlebTools>()
                .AddSingleton<ModelGate>()
                .AddHostedService<CurationJob>()
                .AddSingleton<SearxngSearchSource>()
                .AddSingleton<HackerNewsSearchSource>()
                .AddSingleton<ISearchSource>(services => services.GetRequiredService<SearxngSearchSource>())
                .AddSingleton<ISearchSource>(services => services.GetRequiredService<HackerNewsSearchSource>())
                .AddSingleton<TopicSearch>()
                .AddSingleton<SearchTopicsCommandHandler>()
                .AddSingleton<ResearchTopicNowCommandHandler>()
                .AddSingleton<GetTopicResultsQueryHandler>()
                .AddHostedService<SearchJob>()
                // Singletons, so each keeps its last probe between the status publisher's runs.
                .AddSingleton<SearchSourceHealthCheck<SearxngSearchSource>>()
                .AddSingleton<SearchSourceHealthCheck<HackerNewsSearchSource>>();

            builder.Services.AddHttpClient(SearxngSearchSource.HttpClientName, client => client.Timeout = SearchTimeout);
            // Algolia asks API users to identify themselves; the default HttpClient sends no user agent at all.
            builder.Services.AddHttpClient(HackerNewsSearchSource.HttpClientName, client =>
            {
                client.Timeout = SearchTimeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("KnowledgeBase-Research/1.0 (+https://github.com/DigiBanks99/knowledge-base)");
            });
            builder.Services
                .AddHealthChecks()
                .AddStatusSignal<SearchSourceHealthCheck<SearxngSearchSource>>($"Search: {SearxngSearchSource.SourceName}")
                .AddStatusSignal<SearchSourceHealthCheck<HackerNewsSearchSource>>($"Search: {HackerNewsSearchSource.SourceName}");

            // Only llama-server can count tokens for the model it has loaded; another provider's context is far larger
            // than a curation prompt, so an estimate is enough there.
            bool plebIsLocal = string.Equals(
                builder.Configuration[$"{KnownAgents.Pleb}:{nameof(AgentModelOptions.Provider)}"],
                nameof(ModelProvider.OpenAICompatible),
                StringComparison.OrdinalIgnoreCase);
            if (plebIsLocal)
            {
                builder.Services
                    .AddSingleton<IPromptTokenCounter, LlamaServerTokenCounter>()
                    .AddHttpClient(LlamaServerTokenCounter.HttpClientName, client => client.Timeout = TokenizeTimeout);
            }
            else
            {
                builder.Services.AddSingleton<IPromptTokenCounter, EstimatedTokenCounter>();
            }

            return builder;
        }
    }
}
