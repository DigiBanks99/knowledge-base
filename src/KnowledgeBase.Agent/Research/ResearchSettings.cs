namespace KnowledgeBase.Agent.Research;

/// <summary>
/// How much research work runs, and when. The defaults suit a small local model with one slot on a shared machine;
/// a larger context or a faster server only needs these changed.
/// </summary>
public sealed class ResearchSettings
{
    public const string SectionName = "Research";

    /// <summary>How often a research cycle runs.</summary>
    public TimeSpan CycleInterval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>How long after start the first cycle runs, so a restart doesn't wait a whole interval.</summary>
    public TimeSpan FirstCycleDelay { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The most curation requests the Pleb works through in one cycle.</summary>
    public int CurationsPerCycle { get; init; } = 3;

    /// <summary>How many research agent runs may use the model at once.</summary>
    public int ModelConcurrency { get; init; } = 1;

    /// <summary>
    /// The most tokens a curation prompt may hold. A prompt over it fails rather than letting the server truncate it.
    /// </summary>
    public int CurationPromptTokenBudget { get; init; } = 6000;

    /// <summary>How much of a topic's page the curation prompt includes.</summary>
    public int TopicPageCharacterLimit { get; init; } = 6000;

    /// <summary>How many of a request's changed pages the curation prompt lists.</summary>
    public int ChangedPathsLimit { get; init; } = 20;
}
