using KnowledgeBase.Agent.Facts.Commands;
using KnowledgeBase.Agent.Facts.Queries;
using KnowledgeBase.Agent.Facts.Tools;
using KnowledgeBase.Agent.Facts.Watchers;

namespace KnowledgeBase.Agent.Facts;

internal static class FactsDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddFacts() => builder
            .AddHandlers()
            .AddFactTools()
            .AddWatchers();

        private WebApplicationBuilder AddHandlers()
        {
            builder.Services
                .AddSingleton<IngestFactsCommandHandler>()
                .AddSingleton<ReadFactsQueryHandler>();

            return builder;
        }

        private WebApplicationBuilder AddFactTools()
        {
            builder.Services
                .AddSingleton<FactTools>();

            return builder;
        }

        private WebApplicationBuilder AddWatchers()
        {
            builder.Services
                .AddSingleton<FactWatcher>();

            return builder;
        }
    }
}
