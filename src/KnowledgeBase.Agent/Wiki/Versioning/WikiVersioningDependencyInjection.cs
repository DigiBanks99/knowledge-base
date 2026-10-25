using KnowledgeBase.Agent.Runs;

namespace KnowledgeBase.Agent.Wiki.Versioning;

internal static class WikiVersioningDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddWikiVersioning()
        {
            builder.Services
                .AddSingleton<IGitClient, GitClient>()
                .AddSingleton<InitializeWikiRepositoryCommandHandler>()
                .AddSingleton<CommitWikiRunCommandHandler>()
                .AddSingleton<IAgentRunObserver, WikiVersioningObserver>();

            return builder;
        }
    }
}