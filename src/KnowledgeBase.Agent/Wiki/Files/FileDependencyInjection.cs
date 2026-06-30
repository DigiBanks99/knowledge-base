namespace KnowledgeBase.Agent.Wiki.Files;

internal static class FileDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddFileOperations()
        {
            builder.Services
                .AddSingleton<CreateDirectoryHandler>()
                .AddSingleton<CreateFileHandler>()
                .AddSingleton<ListFilesHandler>()
                .AddSingleton<MoveFileHandler>()
                .AddSingleton<ReadFileHandler>();
            return builder;
        }
    }
}