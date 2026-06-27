using KnowledgeBase.Agent.Wiki.Commands;
using KnowledgeBase.Agent.Wiki.Files;
using KnowledgeBase.Agent.Wiki.Queries;

namespace KnowledgeBase.Agent.Wiki;

internal static class WikiDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddWiki()
        {
            builder
                .AddFileOperations()
                .AddSettings()
                .AddJobs()
                .AddHandlers();

            return builder;
        }

        private WebApplicationBuilder AddSettings()
        {
            IConfigurationSection settingsSection = builder.Configuration
                .GetSection(WikiConfigurationSettings.SectionName);
            builder.Services.Configure<WikiConfigurationSettings>(settingsSection);

            return builder;
        }

        private WebApplicationBuilder AddJobs()
        {
            builder.Services.AddHostedService<InitializeWikiStructureJob>();

            return builder;
        }

        private WebApplicationBuilder AddHandlers()
        {
            builder.Services
                .AddSingleton<CaptureWikiNoteCommandHandler>()
                .AddSingleton<InitializeWikiStructureCommandHandler>()
                .AddSingleton<ListWikiNotesQueryHandler>();

            return builder;
        }
    }
}