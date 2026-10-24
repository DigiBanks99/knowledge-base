using KnowledgeBase.Agent.Agents;

using Microsoft.Extensions.AI;

namespace KnowledgeBase.Agent.Files.Tools;

internal static class FileToolsExtensions
{
    extension(AIAgentToolsBuilder builder)
    {
        public AIAgentToolsBuilder WithFileTools()
        {
            builder.HostBuilder.Services.AddKeyedSingleton(builder.AgentName, (sp, _) =>
            {
                AgentToolCollection tools = new();
                FileTools fileTools = sp.GetRequiredService<FileTools>();
                tools.AddTool(AIFunctionFactory.Create(fileTools.CreateDirectory));
                tools.AddTool(AIFunctionFactory.Create(fileTools.CreateFile));
                tools.AddTool(AIFunctionFactory.Create(fileTools.DeleteFile));
                tools.AddTool(AIFunctionFactory.Create(fileTools.ListFiles));
                tools.AddTool(AIFunctionFactory.Create(fileTools.MoveFile));
                tools.AddTool(AIFunctionFactory.Create(fileTools.PatchFile));
                tools.AddTool(AIFunctionFactory.Create(fileTools.ReadFile));
                tools.AddTool(AIFunctionFactory.Create(fileTools.SearchWiki));
                return tools;
            });
            return builder;
        }

        public AIAgentToolsBuilder WithReadFileTools()
        {
            builder.HostBuilder.Services.AddKeyedSingleton(builder.AgentName, (sp, _) =>
            {
                AgentToolCollection tools = new();
                FileTools fileTools = sp.GetRequiredService<FileTools>();
                tools.AddTool(AIFunctionFactory.Create(fileTools.ReadFile));
                tools.AddTool(AIFunctionFactory.Create(fileTools.SearchWiki));
                tools.AddTool(AIFunctionFactory.Create(fileTools.ListFiles));
                return tools;
            });
            return builder;
        }
    }
}