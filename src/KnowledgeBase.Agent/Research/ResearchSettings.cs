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

    /// <summary>
    /// How long after one search cycle finishes the next starts. Counting from the end rather than on a fixed tick
    /// means a long cycle pushes the next back instead of overlapping it.
    /// </summary>
    public TimeSpan SearchCycleInterval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// The most topics one search cycle searches. Each one's results later cost a Scout run apiece, so this bounds how
    /// much model time a cycle can queue.
    /// </summary>
    public int TopicsPerCycle { get; init; } = 4;

    /// <summary>The most results taken from each source for each query.</summary>
    public int ResultsPerQuery { get; init; } = 10;

    /// <summary>The self-hosted SearXNG instance; SearXNG is not searched when none is configured.</summary>
    public Uri? SearxngEndpoint { get; init; }

    /// <summary>The Hacker News Algolia search API.</summary>
    public Uri HackerNewsEndpoint { get; init; } = new("https://hn.algolia.com/api/v1/");

    /// <summary>
    /// The most tokens of a fetched page kept for triage; longer content is cut at a paragraph. It leaves room in the
    /// Scout's context for its instructions and the topic brief.
    /// </summary>
    public int TriageContentTokenBudget { get; init; } = 2500;

    /// <summary>
    /// Whether the Scout triages fetched results. Off, the search cycle is a dry run: results are searched and fetched
    /// and left pending, to be inspected before the loop closes.
    /// </summary>
    public bool ScoutEnabled { get; init; } = true;

    /// <summary>The most Scout runs (one per result) in one research cycle, so triage never queues unbounded model time.</summary>
    public int ScoutRunsPerCycle { get; init; } = 10;

    /// <summary>
    /// The most tokens a Scout prompt may hold. A prompt over it is not run rather than letting the server truncate it.
    /// </summary>
    public int ScoutPromptTokenBudget { get; init; } = 6000;

    /// <summary>The largest page the fetcher reads; anything larger is recorded as a failed fetch.</summary>
    public long FetchMaxBytes { get; init; } = 2 * 1024 * 1024;

    /// <summary>How long one request of the fetcher may take, from sending it to reading the last byte.</summary>
    public TimeSpan FetchTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>The least time between two requests to the same host.</summary>
    public TimeSpan FetchPolitenessDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How many redirects the fetcher follows for one page.</summary>
    public int FetchMaxRedirects { get; init; } = 5;
}
