using KnowledgeBase.Agent.Health;
using KnowledgeBase.Agent.Notes;
using KnowledgeBase.Agent.Watchers;
using KnowledgeBase.Agent.Wiki.Commands;
using KnowledgeBase.Agent.Wiki.Files;
using KnowledgeBase.Agent.Wiki.Queries;
using KnowledgeBase.Agent.Wiki.Tools;

namespace KnowledgeBase.Agent.Wiki;

internal static class WikiDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddWiki()
        {
            builder
                .AddJobs()
                .AddFileOperations()
                .AddSettings()
                .AddHandlers()
                .AddWatchers()
                .AddTools();

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
                .AddSingleton<ListWikiNotesQueryHandler>()
                .AddSingleton<StartNoteProcessingCommandHandler>();

            return builder;
        }

        private WebApplicationBuilder AddWatchers()
        {
            builder.Services.AddSingleton<NoteWatcher>();

            return builder;
        }

        private WebApplicationBuilder AddTools()
        {
            builder.Services.AddSingleton<FactTools>();

            return builder;
        }
    }

    extension(WebApplication app)
    {
        public WebApplication UseWiki()
        {
            app.Services
                .GetRequiredService<InitializeWikiStructureCommandHandler>()
                .Handle(new InitializeWikiStructureCommand());

            app.Services
                .GetRequiredService<NoteWatcher>()
                .Register();

            app
                .MapNotesEndpoints()
                .MapHealthEndpoints();

            return app;
        }
    }
}