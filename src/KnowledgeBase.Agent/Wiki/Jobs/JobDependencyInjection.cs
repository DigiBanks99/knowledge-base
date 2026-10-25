namespace KnowledgeBase.Agent.Wiki.Jobs;

internal static class JobDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddJobs()
        {
            builder.Services
                .AddSingleton<InitializeWikiStructureJob>();
            return builder;
        }
    }
}
