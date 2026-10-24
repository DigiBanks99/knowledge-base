using KnowledgeBase.Agent.Facts;
using KnowledgeBase.Agent.Facts.Watchers;
using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Health;
using KnowledgeBase.Agent.Notes;
using KnowledgeBase.Agent.Notes.Watchers;
using KnowledgeBase.Agent.Review;
using KnowledgeBase.Agent.Review.Watchers;
using KnowledgeBase.Agent.Wiki.Commands;
using KnowledgeBase.Agent.Wiki.Jobs;
using KnowledgeBase.Agent.Wiki.Tools;

namespace KnowledgeBase.Agent.Wiki;

internal static class WikiDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddWiki() => builder
            .AddFacts()
            .AddFileOperations()
            .AddHandlers()
            .AddJobs()
            .AddNotes()
            .AddTools()
            .AddReview()
            .AddSettings();

        private WebApplicationBuilder AddSettings()
        {
            IConfigurationSection settingsSection = builder.Configuration
                .GetSection(WikiConfigurationSettings.SectionName);
            builder.Services.Configure<WikiConfigurationSettings>(settingsSection);

            return builder;
        }

        private WebApplicationBuilder AddHandlers()
        {
            builder.Services
                .AddSingleton<WikiLock>()
                .AddSingleton<InitializeWikiStructureCommandHandler>()
                .AddSingleton<RecordWikiNoteProcessedCommandHandler>();

            return builder;
        }
    }

    extension(WebApplication app)
    {
        public WebApplication UseWiki()
        {
            app.Services
                .GetRequiredService<InitializeWikiStructureCommandHandler>()
                .Handle(new InitializeWikiStructureCommand(), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            app.Services
                .GetRequiredService<NoteWatcher>()
                .Register();

            app.Services
                .GetRequiredService<FactWatcher>()
                .Register();

            app.Services
                .GetRequiredService<ReviewRequestWatcher>()
                .Register();

            app.Services
                .GetRequiredService<OpenFindingsWatcher>()
                .Register();

            app
                .MapHealthEndpoints()
                .MapNotesEndpoints()
                .MapReviewEndpoints();

            return app;
        }
    }
}