using System.Reflection;
using System.Text;

namespace KnowledgeBase.Agent.Agents;

/// <summary>
/// Composes an agent's system instructions from layers, most general first:
/// the pipeline description (embedded, open-source safe), the shared instructions
/// and wiki rules (configuration, personal) and finally the agent's own role.
/// </summary>
internal static class AgentInstructions
{
    public const string SharedInstructionsKey = "SharedInstructions";
    public const string WikiRulesKey = "WikiRules";

    private const string PipelineResourceName = "KnowledgeBase.Agent.Instructions.pipeline.md";

    private static readonly Lazy<string> Pipeline = new(ReadPipeline);

    public static string Compose(
        IConfiguration configuration,
        string agentName,
        string roleInstructions,
        bool includeWikiRules)
    {
        StringBuilder instructions = new();
        instructions.AppendLine(Pipeline.Value.Trim());

        AppendSection(instructions, "About the Wiki", configuration.GetValue<string>(SharedInstructionsKey));

        if (includeWikiRules)
        {
            string wikiRules = configuration.GetValue<string>(WikiRulesKey)
                ?? throw new InvalidOperationException($"Agent {agentName} requires the '{WikiRulesKey}' configuration value.");
            AppendSection(instructions, "Wiki Rules", wikiRules);
        }

        AppendSection(instructions, $"Your Role: {agentName}", roleInstructions);

        return instructions.ToString();
    }

    /// <summary>
    /// Only the agent's own role, for a small local model whose context is precious and which must not see anything
    /// personal about the author (the research agents write what leaves the machine).
    /// </summary>
    public static string ComposeRoleOnly(string agentName, string roleInstructions)
    {
        StringBuilder instructions = new();
        instructions.AppendLine($"# Your Role: {agentName}");
        instructions.AppendLine();
        instructions.AppendLine(roleInstructions.Trim());
        return instructions.ToString();
    }

    private static void AppendSection(StringBuilder instructions, string heading, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        instructions
            .AppendLine()
            .AppendLine($"# {heading}")
            .AppendLine()
            .AppendLine(content.Trim());
    }

    private static string ReadPipeline()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PipelineResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{PipelineResourceName}' was not found.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
