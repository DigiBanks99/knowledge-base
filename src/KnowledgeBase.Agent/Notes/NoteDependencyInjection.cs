using KnowledgeBase.Agent.Notes.Commands;
using KnowledgeBase.Agent.Notes.Queries;
using KnowledgeBase.Agent.Notes.Watchers;

namespace KnowledgeBase.Agent.Notes;

internal static class NoteDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddNotes() => builder
            .AddHandlers()
            .AddWatchers();

        private WebApplicationBuilder AddHandlers()
        {
            builder.Services
                .AddSingleton<CaptureWikiNoteCommandHandler>()
                .AddSingleton<ListWikiNotesQueryHandler>()
                .AddSingleton<NoteCatalog>()
                .AddSingleton<StartNoteProcessingCommandHandler>();

            return builder;
        }

        private WebApplicationBuilder AddWatchers()
        {
            builder.Services
                .AddSingleton<NoteWatcher>();

            return builder;
        }
    }
}