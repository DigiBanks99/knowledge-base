using System.Text;

using KnowledgeBase.Agent.Health;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Commands;

using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.IntegrationTests;

/// <summary>
/// The API's services over a wiki root in a temporary directory, with every agent replaced by a <see cref="FakeAgent"/>.
/// Nothing runs until a test calls <see cref="InitialiseAsync"/> or <see cref="Start"/>, so a test can lay out the
/// files a restart would find first.
/// </summary>
internal sealed class TestWiki : IAsyncDisposable
{
    private readonly TemporaryDirectory _root = new();
    private readonly ProblemLog _problems = new();
    private readonly WebApplication _app;

    /// <param name="settings">More configuration for the API, such as a backup remote.</param>
    public TestWiki(IReadOnlyDictionary<string, string?>? settings = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(settings ?? new Dictionary<string, string?>())
        {
            [$"{WikiConfigurationSettings.SectionName}:{nameof(WikiConfigurationSettings.RootDirectory)}"] = _root.Path
        });
        builder.Services.AddLogging(logging => logging.AddProvider(_problems));
        builder.Services.AddSingleton<IServer, NoServer>();
        // Timers run ten times faster: the Editor's 5 second debounce takes half a second, which still leaves room for
        // a review's findings to be written before it ends.
        builder.Services.AddSingleton<TimeProvider>(new AcceleratedTimeProvider(10));
        builder
            .AddWiki()
            .AddHealthChecks();

        foreach (FakeAgent agent in Agents)
        {
            builder.Services.AddKeyedSingleton<AIAgent>(agent.Name, agent);
        }

        // Searches go to a scripted source rather than SearXNG or Hacker News.
        builder.Services.RemoveAll<ISearchSource>();
        builder.Services.AddSingleton<ISearchSource>(Search);

        _app = builder.Build();
        Settings = _app.Services.GetRequiredService<IOptions<WikiConfigurationSettings>>().Value;
    }

    public WikiConfigurationSettings Settings { get; }

    public FakeAgent FactFinder { get; } = new(KnownAgents.FactFinder);
    public FakeAgent ThreadWeaver { get; } = new(KnownAgents.ThreadWeaver);
    public FakeAgent Reviewer { get; } = new(KnownAgents.Reviewer);
    public FakeAgent Editor { get; } = new(KnownAgents.Editor);
    public FakeAgent Librarian { get; } = new(KnownAgents.Librarian);
    public FakeAgent Pleb { get; } = new(KnownAgents.Pleb);

    public FakeAgent Scout { get; } = new(KnownAgents.Scout);

    /// <summary>The only search source, which a test scripts.</summary>
    public FakeSearchSource Search { get; } = new();

    private IEnumerable<FakeAgent> Agents => [FactFinder, ThreadWeaver, Reviewer, Editor, Librarian, Pleb, Scout];

    public T Get<T>() where T : notnull => _app.Services.GetRequiredService<T>();

    /// <summary>Creates the folder structure and seeds the index pages, as the app does before anything else.</summary>
    public async Task InitialiseAsync()
    {
        Result<bool, Error> result = await Get<InitializeWikiStructureCommandHandler>()
            .Handle(new InitializeWikiStructureCommand(), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.ToString() : null);
    }

    /// <summary>
    /// Starts the app the way <c>Program</c> does: prepares the folders, fails interrupted runs, requeues interrupted
    /// notes and starts the watchers.
    /// </summary>
    public void Start() => _app.UseWiki();

    /// <summary>
    /// Calls a tool the way a model-backed agent does, recording the call on the current run like the agent's tool
    /// middleware in <c>AgentConfiguration</c>.
    /// </summary>
    public async Task<T> CallToolAsync<T>(string tool, string? target, Func<Task<T>> call)
    {
        AgentRunTracker runTracker = Get<AgentRunTracker>();
        try
        {
            T result = await call();
            await runTracker.RecordToolCallAsync(tool, target, null);
            return result;
        }
        catch (Exception ex)
        {
            await runTracker.RecordToolCallAsync(tool, target, ex.Message);
            throw;
        }
    }

    /// <summary>Writes a file below the root directory, creating its folder if needed.</summary>
    public string Write(string directory, string name, string content)
    {
        Directory.CreateDirectory(directory);
        string fullPath = Path.Combine(directory, name);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public Task<IReadOnlyList<AgentRun>> RunsAsync() =>
        Get<AgentRunStore>().ListAsync(AgentRunFilter.All, 0, 100, CancellationToken.None);

    /// <summary>
    /// Whether every run recorded so far has finished, and, when <paramref name="expected"/> is given, that there are
    /// that many, so their records can be asserted on.
    /// </summary>
    public async Task<bool> RunsFinishedAsync(int? expected = null)
    {
        IReadOnlyList<AgentRun> runs = await RunsAsync();
        return (expected is null || runs.Count == expected) && runs.All(run => run.Status != AgentRunStatus.Running);
    }

    /// <summary>
    /// Everything in the pipeline folders, every run and the warnings and errors logged, to explain a test that timed
    /// out waiting for the pipeline to settle.
    /// </summary>
    public async Task<string> DescribeAsync()
    {
        StringBuilder description = new();
        description.AppendLine("The pipeline did not settle. Its state:");
        string[] directories =
        [
            Settings.NewNotesDirectory, Settings.ProcessingNotesDirectory, Settings.ProcessedNotesDirectory,
            Settings.FailedNotesDirectory, Settings.FactsDirectory, Settings.ProcessedFactsDirectory,
            Settings.FailedFactsDirectory, Settings.ReviewRequestDirectory, Settings.CompletedReviewRequestDirectory,
            Settings.ReviewOpenFindingsDirectory, Settings.ReviewClosedFindingsDirectory
        ];
        foreach (string directory in directories)
        {
            description.AppendLine($"  {Path.GetRelativePath(_root.Path, directory)}: [{string.Join(", ", FileNames(directory, "*.*"))}]");
        }

        foreach (AgentRun run in await RunsAsync())
        {
            description.AppendLine($"  run {run.Agent} {run.Status} ({run.Trigger.Kind} {run.Trigger.Reference}): {run.ToolCalls.Count} tool calls {run.Error}");
        }

        foreach (FakeAgent agent in Agents.Where(agent => agent.Prompts.Count > 0))
        {
            description.AppendLine($"  {agent.Name} was prompted {agent.Prompts.Count} times");
        }

        foreach (string problem in _problems.Entries)
        {
            description.AppendLine($"  {problem}");
        }

        return description.ToString();
    }

    public static IReadOnlyList<string> FileNames(string directory, string pattern = "*") =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern).Select(Path.GetFileName).OfType<string>().Order().ToList()
            : [];

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _root.Dispose();
    }

    /// <summary>The app is never started, but building it requires a server.</summary>
    private sealed class NoServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull => throw new NotSupportedException("The test app does not serve requests.");

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
