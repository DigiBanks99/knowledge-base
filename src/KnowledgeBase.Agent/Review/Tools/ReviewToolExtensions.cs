using KnowledgeBase.Agent.Anthropic;
using KnowledgeBase.Agent.Files.Tools;

using Microsoft.Extensions.AI;

namespace KnowledgeBase.Agent.Review.Tools;

internal static class ReviewToolExtensions
{
    extension(AIAgentToolsBuilder agentToolsBuilder)
    {
        public AIAgentToolsBuilder WithReviewTools()
        {
            agentToolsBuilder.HostBuilder.Services.AddKeyedSingleton(agentToolsBuilder.AgentName, (sp, _) =>
            {
                AgentToolCollection tools = new();
                FileTools fileTools = sp.GetRequiredService<FileTools>();
                ReviewTools reviewTools = sp.GetRequiredService<ReviewTools>();
                tools.AddTool(AIFunctionFactory.Create(fileTools.ReadFile));
                tools.AddTool(AIFunctionFactory.Create(fileTools.SearchWiki));
                tools.AddTool(AIFunctionFactory.Create(fileTools.ListFiles));
                tools.AddTool(AIFunctionFactory.Create(reviewTools.ListFindings));
                tools.AddTool(AIFunctionFactory.Create(reviewTools.WriteFindings));
                return tools;
            });
            return agentToolsBuilder;
        }

        public AIAgentToolsBuilder WithEditorTools()
        {
            agentToolsBuilder.HostBuilder.Services.AddKeyedSingleton(agentToolsBuilder.AgentName, (sp, _) =>
            {
                AgentToolCollection tools = new();
                FileTools fileTools = sp.GetRequiredService<FileTools>();
                ReviewTools reviewTools = sp.GetRequiredService<ReviewTools>();
                tools.AddTool(AIFunctionFactory.Create(fileTools.CreateDirectory));
                tools.AddTool(AIFunctionFactory.Create(fileTools.CreateFile));
                tools.AddTool(AIFunctionFactory.Create(fileTools.DeleteFile));
                tools.AddTool(AIFunctionFactory.Create(fileTools.ListFiles));
                tools.AddTool(AIFunctionFactory.Create(fileTools.MoveFile));
                tools.AddTool(AIFunctionFactory.Create(fileTools.PatchFile));
                tools.AddTool(AIFunctionFactory.Create(fileTools.ReadFile));
                tools.AddTool(AIFunctionFactory.Create(fileTools.SearchWiki));
                tools.AddTool(AIFunctionFactory.Create(reviewTools.ListFindings));
                tools.AddTool(AIFunctionFactory.Create(reviewTools.ResolveFindings));
                return tools;
            });
            return agentToolsBuilder;
        }
    }
}