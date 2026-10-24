using KnowledgeBase.Agent.Files.Tools;

namespace KnowledgeBase.Agent.Files;

internal static class FileDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddFileOperations()
        {
            builder.Services.AddSingleton<FileTools>();

            builder.Services
                .AddSingleton<CreateDirectoryHandler>()
                .AddSingleton<CreateFileHandler>()
                .AddSingleton<DeleteFileHandler>()
                .AddSingleton<ListFilesHandler>()
                .AddSingleton<MoveFileHandler>()
                .AddSingleton<PatchFileHandler>()
                .AddSingleton<ReadFileHandler>();
            return builder;
        }
    }
}