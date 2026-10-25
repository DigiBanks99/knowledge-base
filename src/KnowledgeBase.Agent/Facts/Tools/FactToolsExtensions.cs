using KnowledgeBase.Agent.Agents;

using Microsoft.Extensions.AI;

namespace KnowledgeBase.Agent.Facts.Tools;

internal static class FactToolsExtensions
{
    extension(AIAgentToolsBuilder builder)
    {
        public AIAgentToolsBuilder WithFactTools()
        {
            builder.HostBuilder.Services.AddKeyedSingleton(builder.AgentName, (sp, _) =>
            {
                AgentToolCollection tools = new();
                FactTools factTools = sp.GetRequiredService<FactTools>();
                tools.AddTool(AIFunctionFactory.Create(factTools.FactWriterTool));
                return tools;
            });
            return builder;
        }
    }
}
