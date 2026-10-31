using KnowledgeBase.Agent.Agents;

using Microsoft.Extensions.AI;

namespace KnowledgeBase.Agent.Research.Tools;

internal static class PlebToolsExtensions
{
    extension(AIAgentToolsBuilder builder)
    {
        public AIAgentToolsBuilder WithPlebTools()
        {
            builder.HostBuilder.Services.AddKeyedSingleton(builder.AgentName, (sp, _) =>
            {
                AgentToolCollection tools = new();
                PlebTools plebTools = sp.GetRequiredService<PlebTools>();
                tools.AddTool(AIFunctionFactory.Create(plebTools.SetTopicQueries));
                tools.AddTool(AIFunctionFactory.Create(plebTools.KeepQueries));
                tools.AddTool(AIFunctionFactory.Create(plebTools.RequestHumanReview));
                return tools;
            });
            return builder;
        }
    }
}
