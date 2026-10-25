using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Status;

namespace KnowledgeBase.Agent.Wiki.Versioning;

internal static class WikiVersioningDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddWikiVersioning()
        {
            builder.Services
                .Configure<WikiBackupSettings>(builder.Configuration.GetSection(WikiBackupSettings.SectionName))
                .AddSingleton<IGitClient, GitClient>()
                .AddSingleton<WikiBackupState>()
                .AddSingleton<InitializeWikiRepositoryCommandHandler>()
                .AddSingleton<CommitWikiRunCommandHandler>()
                .AddSingleton<PushWikiCommandHandler>()
                .AddSingleton<IAgentRunObserver, WikiVersioningObserver>();

            builder.Services
                .AddHealthChecks()
                .AddStatusSignal<WikiBackupHealthCheck>("Backup");

            return builder;
        }
    }
}