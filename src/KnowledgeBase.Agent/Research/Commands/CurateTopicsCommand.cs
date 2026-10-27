using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Curation;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Runs;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Status;

using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>Works through some of the curation queue: one research cycle's worth of the Pleb's work.</summary>
public record struct CurateTopicsCommand;

/// <summary>What one curation cycle did, by topic.</summary>
/// <param name="Skipped">Whether the cycle was skipped because the model server was not healthy.</param>
/// <param name="LeftWaiting">Topics whose curation could not run this time and stays queued.</param>
public sealed record CurationCycle(
    bool Skipped,
    IReadOnlyList<string> Curated,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> LeftWaiting)
{
    public static CurationCycle SkippedCycle { get; } = new(true, [], [], []);
}

internal sealed class CurateTopicsCommandHandler(
    ILogger<CurateTopicsCommandHandler> logger,
    IOptions<ResearchSettings> settings,
    IResearchStore store,
    IWikiTopics wikiTopics,
    IModelServerHealth modelServer,
    IPromptTokenCounter tokenCounter,
    ModelGate modelGate,
    AgentRunTracker runTracker,
    [FromKeyedServices(KnownAgents.Pleb)] AIAgent pleb,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    private static readonly HashSet<string> VerdictTools = [nameof(PlebTools.SetTopicQueries), nameof(PlebTools.KeepQueries)];

    private readonly ResearchSettings _settings = settings.Value;

    // Sent with every prompt, so it counts towards the budget too. The tool schemas and chat template add a little
    // more that is not counted, which the budget's headroom below the context size covers.
    private readonly string _instructions = AgentInstructions.ComposeRoleOnly(
        KnownAgents.Pleb,
        configuration[$"{KnownAgents.Pleb}:{nameof(AgentModelOptions.Instructions)}"] ?? string.Empty);

    private enum Outcome
    {
        Curated,
        Failed,
        LeftWaiting
    }

    /// <summary>
    /// The oldest requests go first, up to <see cref="ResearchSettings.CurationsPerCycle"/>, so adopting research (a
    /// request for every topic) drains gradually. Muted topics' requests keep waiting until they are unmuted. Nothing
    /// runs while the model server is unhealthy, so an outage doesn't turn the queue into failures.
    /// </summary>
    public async Task<Result<CurationCycle, Error>> Handle(CurateTopicsCommand _, CancellationToken cancellationToken)
    {
        if (!await modelServer.IsAvailableToAsync(KnownAgents.Pleb, cancellationToken))
        {
            logger.CurationSkippedModelServerUnhealthy();
            return CurationCycle.SkippedCycle;
        }

        return await store
            .ListTopicsAsync(cancellationToken)
            .Bind(async topics => await store
                .ListCurationRequestsAsync(cancellationToken)
                .Bind(requests => CurateAsync(topics, requests, cancellationToken)));
    }

    private async Task<Result<CurationCycle, Error>> CurateAsync(
        IReadOnlyList<ResearchTopic> topics,
        IReadOnlyList<CurationRequest> requests,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ResearchTopic> topicsByPath = topics.ToDictionary(topic => topic.Path, StringComparer.Ordinal);
        (ResearchTopic Topic, CurationRequest Request)[] due =
        [
            .. requests
                .Where(request => topicsByPath.TryGetValue(request.Topic, out ResearchTopic? topic) && !topic.Muted)
                .OrderBy(request => request.QueuedSince)
                .Take(Math.Max(0, _settings.CurationsPerCycle))
                .Select(request => (topicsByPath[request.Topic], request))
        ];
        if (due.Length > 0)
        {
            logger.CuratingTopics(due.Length, requests.Count);
        }

        List<(string Topic, Outcome Outcome)> outcomes = [];
        List<Error> errors = [];
        foreach ((ResearchTopic topic, CurationRequest request) in due)
        {
            Result<Outcome, Error> outcome = await modelGate.RunAsync(token => CurateAsync(topic, request, token), cancellationToken);
            if (outcome.IsSuccess)
            {
                outcomes.Add((topic.Path, outcome.Value));
            }
            else
            {
                errors.Add(outcome.Error);
            }
        }

        return errors.Count switch
        {
            0 => new CurationCycle(false, Topics(Outcome.Curated), Topics(Outcome.Failed), Topics(Outcome.LeftWaiting)),
            1 => errors[0],
            _ => new AggregateError(errors)
        };

        string[] Topics(Outcome kind) => [.. outcomes.Where(outcome => outcome.Outcome == kind).Select(outcome => outcome.Topic)];
    }

    /// <summary>
    /// A curation succeeds only when the Pleb set or kept the queries; a run that ended without either is recorded as
    /// failed. When the prompt cannot be counted or the model timed out, the request stays queued for the next cycle
    /// instead, as the fault is the server's rather than the curation's.
    /// </summary>
    private async Task<Result<Outcome, Error>> CurateAsync(ResearchTopic topic, CurationRequest request, CancellationToken cancellationToken)
    {
        string prompt = CurationPrompt.Build(topic, request, wikiTopics, _settings);
        Result<int, Error> tokens = await tokenCounter.CountAsync($"{_instructions}\n{prompt}", cancellationToken);
        if (tokens.IsFailure)
        {
            logger.CurationLeftWaiting(topic.Path, tokens.Error);
            return Outcome.LeftWaiting;
        }

        if (tokens.Value > _settings.CurationPromptTokenBudget)
        {
            return await FailAsync(
                request,
                $"The curation prompt is {tokens.Value} tokens, over the budget of {_settings.CurationPromptTokenBudget}. Shorten the topic's page or raise {ResearchSettings.SectionName}:{nameof(ResearchSettings.CurationPromptTokenBudget)}.",
                cancellationToken);
        }

        bool timedOut = false;
        Result<AgentRun, Error> run = await runTracker.RunAsync(
            KnownAgents.Pleb,
            new AgentRunTrigger(AgentRunTriggerKind.Curation, TopicPaths.Slug(topic.Path)),
            FactOrigin.Author,
            topic.Path,
            async token =>
            {
                AgentResponse response;
                try
                {
                    response = await pleb.RunAsync(prompt, cancellationToken: token);
                }
                catch (Exception ex) when (IsTimeout(ex) && !token.IsCancellationRequested)
                {
                    timedOut = true;
                    throw;
                }

                return HasVerdict(runTracker.Current)
                    ? response
                    : throw new InvalidOperationException(
                        $"The Pleb ended without a verdict: it must call `{nameof(PlebTools.SetTopicQueries)}` or `{nameof(PlebTools.KeepQueries)}` successfully.");
            },
            cancellationToken);

        // The verdict is stored as the tool is called, so a run that fails afterwards (its last turn, say) has still
        // curated the topic.
        if (run.IsSuccess || (run.Error is AgentRunFailedError failed && HasVerdict(failed.Run)))
        {
            return await store.CompleteCurationAsync(request, cancellationToken).Map(() => Outcome.Curated);
        }

        if (timedOut)
        {
            logger.CurationLeftWaiting(topic.Path, run.Error);
            return Outcome.LeftWaiting;
        }

        return await FailAsync(request, run.Error.Message, cancellationToken);
    }

    private async Task<Result<Outcome, Error>> FailAsync(CurationRequest request, string reason, CancellationToken cancellationToken)
    {
        logger.CurationFailed(request.Topic, reason);
        return await store
            .FailCurationAsync(new FailedCuration(request, reason, timeProvider.GetUtcNow()), cancellationToken)
            .Map(() => Outcome.Failed);
    }

    private static bool HasVerdict(AgentRun? run) =>
        run?.ToolCalls.Any(call => call.Succeeded && VerdictTools.Contains(call.Tool)) == true;

    private static bool IsTimeout(Exception ex) =>
        ex is TimeoutException or TaskCanceledException || ex.InnerException is TimeoutException;
}
