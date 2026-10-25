using KnowledgeBase.Agent.Events;

namespace KnowledgeBase.Agent.Runs;

internal static class RunsDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddRuns()
        {
            builder.Services
                .AddSingleton<AgentRunStore>()
                .AddSingleton<IAgentRunStore>(services => services.GetRequiredService<AgentRunStore>())
                .AddSingleton<AgentRunTracker>()
                .AddSingleton<ChangeNotifier>()
                .AddSingleton<IChangePublisher>(services => services.GetRequiredService<ChangeNotifier>());

            return builder;
        }
    }
}