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

    /// <summary>
    /// How many of the author's clarifications and rejections a topic keeps for its curation prompts; older ones are
    /// dropped.
    /// </summary>
    public int GuidanceLimit { get; init; } = 5;

    /// <summary>How many of a request's changed pages the curation prompt lists.</summary>
    public int ChangedPathsLimit { get; init; } = 20;

    /// <summary>
    /// How long after one search cycle finishes the next starts. Counting from the end rather than on a fixed tick
    /// means a long cycle pushes the next back instead of overlapping it.
    /// </summary>
    public TimeSpan SearchCycleInterval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// The most topics one search cycle searches. Each one's results later cost Keeper calls, so this bounds how
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
    /// Keeper's context for the topic brief and its questions.
    /// </summary>
    public int TriageContentTokenBudget { get; init; } = 2500;

    /// <summary>
    /// Whether the Keeper and the Clerk triage fetched results. Off, the search cycle is a dry run: results are searched
    /// and fetched and left pending, to be inspected before the loop closes.
    /// </summary>
    public bool TriageEnabled { get; init; } = true;

    /// <summary>
    /// The Keeper's decision model. On the shared llama-server in router mode it is the preset's section name.
    /// </summary>
    public string KeeperModel { get; init; } = "laya";

    /// <summary>
    /// The base address of a server of the Keeper's own, including the version segment (e.g.
    /// <c>http://127.0.0.1:9932/v1</c>). Unset, the Keeper uses the <c>OpenAICompatible</c> server and its key.
    /// </summary>
    public Uri? KeeperEndpoint { get; init; }

    /// <summary>The key of <see cref="KeeperEndpoint"/>'s server, if it was started with one.</summary>
    public string? KeeperApiKey { get; init; }

    /// <summary>
    /// How long one Keeper call may take. Measured on a 4-core VM, the p95 was about a minute and the slowest 66
    /// seconds, so this leaves room above both.
    /// </summary>
    public TimeSpan KeeperTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// The least <c>useful</c> probability a result needs to be kept. Provisional: measured only against noise, which
    /// scored up to 0.52. Changing the Keeper's question wording means tuning it again.
    /// </summary>
    public double KeepThreshold { get; init; } = 0.6;

    /// <summary>
    /// The <c>injection</c> probability at which a result is discarded as instructions aimed at an AI. High because the
    /// signal is weak (harmless pages scored up to 0.84): the Keeper is a filter, not the security boundary.
    /// </summary>
    public double InjectionThreshold { get; init; } = 0.9;

    /// <summary>The most Keeper calls in one research cycle, so triage never queues unbounded model time.</summary>
    public int KeeperCallsPerCycle { get; init; } = 20;

    /// <summary>
    /// The most tokens of a page's paragraphs sent to the Keeper. Every question rereads the page, and latency grows
    /// with it (about 3 seconds at 0.8k tokens, a minute at 10k), so the cap keeps calls near the median. Pages are
    /// already cut to <see cref="TriageContentTokenBudget"/> when fetched, so this only cuts once that is raised above
    /// it (or for a page stored before it was lowered).
    /// </summary>
    public int KeeperContentTokenBudget { get; init; } = 4500;

    /// <summary>How many of a page's paragraphs the Keeper may choose its quotes from.</summary>
    public int MaxQuoteOptions { get; init; } = 50;

    /// <summary>
    /// The least probability a quote needs to go in the note. The first quote is usually above 0.9 and later ones
    /// near 0, so this drops quotes the model barely chose. Proposed, not measured.
    /// </summary>
    public double QuoteProbabilityFloor { get; init; } = 0.1;

    /// <summary>
    /// How many failed Keeper calls a result gets before it is discarded as failed, so a page the model can never
    /// answer doesn't hold up its batch.
    /// </summary>
    public int KeeperMaxAttempts { get; init; } = 3;

    /// <summary>
    /// The most tokens a Clerk prompt may hold. A prompt over it is not run rather than letting the server truncate it,
    /// and its result keeps its quotes alone.
    /// </summary>
    public int ClerkPromptTokenBudget { get; init; } = 6000;

    /// <summary>The most Clerk runs in one research cycle, so summaries never queue unbounded model time.</summary>
    public int ClerkRunsPerCycle { get; init; } = 10;

    /// <summary>
    /// How many Clerk runs for a kept result may time out before it is given up on and keeps its quotes alone, so a
    /// page the model never finishes doesn't hold up its batch.
    /// </summary>
    public int ClerkMaxAttempts { get; init; } = 3;

    /// <summary>The largest page the fetcher reads; anything larger is recorded as a failed fetch.</summary>
    public long FetchMaxBytes { get; init; } = 2 * 1024 * 1024;

    /// <summary>How long one request of the fetcher may take, from sending it to reading the last byte.</summary>
    public TimeSpan FetchTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>The least time between two requests to the same host.</summary>
    public TimeSpan FetchPolitenessDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How many redirects the fetcher follows for one page.</summary>
    public int FetchMaxRedirects { get; init; } = 5;
}
