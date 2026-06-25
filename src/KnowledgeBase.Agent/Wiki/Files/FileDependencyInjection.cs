namespace KnowledgeBase.Agent.Wiki.Files;

internal static class FileDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddFileOperations()
        {
            builder.Services.AddSingleton<CreateFileHandler>();
            return builder;
        }
    }
}