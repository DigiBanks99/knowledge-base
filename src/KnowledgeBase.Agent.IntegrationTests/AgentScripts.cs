using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Facts.Tools;

namespace KnowledgeBase.Agent.IntegrationTests;

/// <summary>Scripted agent behaviour shared by the pipeline tests.</summary>
internal static class AgentScripts
{
    public static readonly Fact[] SampleFacts =
    [
        new(
            new FactSubject("Susan", "A colleague on the platform team.", SubjectKind.Person),
            [new Observation("Gave feedback on the design review", "Asked for clearer trade-offs.", 2, [])])
    ];

    extension(TestWiki wiki)
    {
        /// <summary>Makes the FactFinder record <see cref="SampleFacts"/> under the given collection name.</summary>
        public void FactFinderWritesFacts(string collectionName) =>
            wiki.FactFinder.Behaviour = async (_, cancellationToken) => await wiki.CallToolAsync(
                nameof(FactTools.FactWriterTool),
                collectionName,
                () => wiki.Get<FactTools>().FactWriterTool(SampleFacts, collectionName, cancellationToken));
    }

    extension(FakeAgent agent)
    {
        public void Fails(string message) =>
            agent.Behaviour = (_, _) => throw new HttpRequestException(message);
    }
}
