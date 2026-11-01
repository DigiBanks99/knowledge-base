using KnowledgeBase.Agent.Agents;

using Microsoft.Extensions.AI;

namespace KnowledgeBase.Agent.Research.Tools;

internal static class ClerkToolsExtensions
{
    extension(AIAgentToolsBuilder builder)
    {
        public AIAgentToolsBuilder WithClerkTools()
        {
            builder.HostBuilder.Services.AddKeyedSingleton(builder.AgentName, (sp, _) =>
            {
                AgentToolCollection tools = new();
                ClerkTools clerkTools = sp.GetRequiredService<ClerkTools>();
                tools.AddTool(AIFunctionFactory.Create(clerkTools.RecordSummary));
                return tools;
            });
            return builder;
        }
    }
}
