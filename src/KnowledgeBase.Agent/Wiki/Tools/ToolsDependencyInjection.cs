using KnowledgeBase.Agent.Facts.Tools;
using KnowledgeBase.Agent.Files.Tools;

namespace KnowledgeBase.Agent.Wiki.Tools;

internal static class ToolsDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddTools()
        {
            builder.Services
                .AddSingleton<DateTools>();

            return builder;
        }
    }
}
