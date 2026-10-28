using KnowledgeBase.Agent.Agents;

using Microsoft.Extensions.AI;

namespace KnowledgeBase.Agent.Research.Tools;

internal static class ScoutToolsExtensions
{
    extension(AIAgentToolsBuilder builder)
    {
        public AIAgentToolsBuilder WithScoutTools()
        {
            builder.HostBuilder.Services.AddKeyedSingleton(builder.AgentName, (sp, _) =>
            {
                AgentToolCollection tools = new();
                ScoutTools scoutTools = sp.GetRequiredService<ScoutTools>();
                tools.AddTool(AIFunctionFactory.Create(scoutTools.KeepResult));
                tools.AddTool(AIFunctionFactory.Create(scoutTools.DiscardResult));
                return tools;
            });
            return builder;
        }
    }
}
