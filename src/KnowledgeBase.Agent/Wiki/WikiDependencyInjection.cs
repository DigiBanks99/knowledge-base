using KnowledgeBase.Agent.Wiki.Commands;
using KnowledgeBase.Agent.Wiki.Files;

namespace KnowledgeBase.Agent.Wiki;

internal static class WikiDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddWiki()
        {
            builder.AddFileOperations();

            builder.Services
                .Configure<WikiConfigurationSettings>(builder.Configuration.GetSection(WikiConfigurationSettings.SectionName))
                .AddSingleton<CreateWikiSourceCommandHandler>();

            return builder;
        }
    }
}