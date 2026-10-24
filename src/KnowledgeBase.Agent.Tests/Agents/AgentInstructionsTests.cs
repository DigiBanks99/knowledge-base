using KnowledgeBase.Agent.Agents;

using Microsoft.Extensions.Configuration;

namespace KnowledgeBase.Agent.Tests.Agents;

public sealed class AgentInstructionsTests
{
    private const string AgentName = "Editor";
    private const string RoleInstructions = "Fix what the reviewer found.";
    private const string WikiRules = "Link every person to their page under people/.";

    [Test]
    public void AnAgentThatEditsTheWikiIsInstructed()
    {
        IConfiguration configuration = Configuration(sharedInstructions: "  Shared instructions.  ", wikiRules: WikiRules);

        string instructions = AgentInstructions.Compose(configuration, AgentName, RoleInstructions, includeWikiRules: true);

        ItShouldLayerTheSectionsFromGeneralToSpecific();
        ItShouldTrimEachSection();

        void ItShouldLayerTheSectionsFromGeneralToSpecific()
        {
            int about = instructions.IndexOf("# About the Wiki", StringComparison.Ordinal);
            int rules = instructions.IndexOf("# Wiki Rules", StringComparison.Ordinal);
            int role = instructions.IndexOf($"# Your Role: {AgentName}", StringComparison.Ordinal);

            about.ShouldBeGreaterThan(0, "The embedded pipeline description comes first.");
            rules.ShouldBeGreaterThan(about);
            role.ShouldBeGreaterThan(rules);
            instructions.TrimEnd().ShouldEndWith(RoleInstructions);
        }

        void ItShouldTrimEachSection() => instructions.ShouldContain(
            $"# About the Wiki{Environment.NewLine}{Environment.NewLine}Shared instructions.{Environment.NewLine}");
    }

    [Test]
    public void AnAgentThatDoesNotNeedTheWikiRulesIsInstructed()
    {
        IConfiguration configuration = Configuration(sharedInstructions: "Shared instructions.", wikiRules: WikiRules);

        string instructions = AgentInstructions.Compose(configuration, AgentName, RoleInstructions, includeWikiRules: false);

        ItShouldLeaveOutTheWikiRules();

        void ItShouldLeaveOutTheWikiRules()
        {
            instructions.ShouldNotContain("# Wiki Rules");
            instructions.ShouldNotContain(WikiRules);
        }
    }

    [Test]
    public void NoSharedInstructionsAreConfigured()
    {
        IConfiguration configuration = Configuration(sharedInstructions: " ", wikiRules: null);

        string instructions = AgentInstructions.Compose(configuration, AgentName, RoleInstructions, includeWikiRules: false);

        ItShouldLeaveOutTheEmptySection();

        void ItShouldLeaveOutTheEmptySection() => instructions.ShouldNotContain("# About the Wiki");
    }

    [Test]
    public void TheWikiRulesAreMissing()
    {
        IConfiguration configuration = Configuration(sharedInstructions: "Shared instructions.", wikiRules: null);

        Exception? exception = Catch.Exception(() =>
            AgentInstructions.Compose(configuration, AgentName, RoleInstructions, includeWikiRules: true));

        ItShouldRefuseToInstructTheAgent();

        void ItShouldRefuseToInstructTheAgent() => exception
            .ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldBe($"Agent {AgentName} requires the '{AgentInstructions.WikiRulesKey}' configuration value.");
    }

    private static IConfiguration Configuration(string? sharedInstructions, string? wikiRules) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AgentInstructions.SharedInstructionsKey] = sharedInstructions,
                [AgentInstructions.WikiRulesKey] = wikiRules
            })
            .Build();
}