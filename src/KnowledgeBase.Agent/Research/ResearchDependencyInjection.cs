using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Curation;
using KnowledgeBase.Agent.Research.Fetch;
using KnowledgeBase.Agent.Research.Jobs;
using KnowledgeBase.Agent.Research.Queries;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Research.Triage;
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
                .AddSingleton<AddDenyListEntryCommandHandler>()
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
                .AddSingleton<ScoutTools>()
                .AddSingleton<IResearchNoteWriter, ResearchNoteWriter>()
                .AddSingleton<TriageResultsCommandHandler>()
                .AddSingleton<ResolveAwaitingBatchesCommandHandler>()
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
                .AddSingleton<IHostResolver, DnsHostResolver>()
                .AddSingleton<FetchTarget>()
                .AddSingleton<PolitenessGate>()
                .AddSingleton<IContentExtractor, ReadabilityExtractor>()
                .AddSingleton<ContentBudget>()
                .AddSingleton<IPageFetcher, PageFetcher>()
                .AddSingleton<PendingResultFetch>()
                .AddSingleton<FetchPendingResultsCommandHandler>()
                .AddHostedService<SearchJob>()
                // Singletons, so each keeps its last probe between the status publisher's runs.
                .AddSingleton<SearchSourceHealthCheck<SearxngSearchSource>>()
                .AddSingleton<SearchSourceHealthCheck<HackerNewsSearchSource>>()
                .AddSingleton<ResearchCycleClock>();

            builder.Services.AddHttpClient(SearxngSearchSource.HttpClientName, client => client.Timeout = SearchTimeout);
            // Algolia asks API users to identify themselves; the default HttpClient sends no user agent at all.
            builder.Services.AddHttpClient(HackerNewsSearchSource.HttpClientName, client =>
            {
                client.Timeout = SearchTimeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(PageFetcher.UserAgent);
            });
            // The fetcher bounds each request itself (and re-arms the bound per redirect), so the client's own timeout
            // is off; its handler is the SSRF guard's, which connects only to vetted public addresses.
            builder.Services
                .AddHttpClient(PageFetcher.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(services => services.GetRequiredService<FetchTarget>().CreateHandler());
            builder.Services
                .AddHealthChecks()
                .AddStatusSignal<SearchSourceHealthCheck<SearxngSearchSource>>($"Search: {SearxngSearchSource.SourceName}")
                .AddStatusSignal<SearchSourceHealthCheck<HackerNewsSearchSource>>($"Search: {HackerNewsSearchSource.SourceName}")
                .AddStatusSignal<ResearchLoopHealthCheck>(ResearchLoopHealthCheck.Name);

            // Only llama-server can count tokens for the model it has loaded; another provider's context is far larger
            // than a curation prompt, so an estimate is enough there.
            bool researchIsLocal = new[] { KnownAgents.Pleb, KnownAgents.Scout }.Any(agent => string.Equals(
                builder.Configuration[$"{agent}:{nameof(AgentModelOptions.Provider)}"],
                nameof(ModelProvider.OpenAICompatible),
                StringComparison.OrdinalIgnoreCase));
            if (researchIsLocal)
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
