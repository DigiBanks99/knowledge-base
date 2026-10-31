using KnowledgeBase.Agent.Files.Tools;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>Keeps the research topics in step with the wiki pages an agent run changed.</summary>
public record RecordTopicChangesCommand(AgentRun Run);

/// <summary>What recording a run's changes did to the topics.</summary>
public sealed record TopicChanges(
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Pruned,
    IReadOnlyList<string> CurationRequested);

internal sealed class RecordTopicChangesCommandHandler(
    ILogger<RecordTopicChangesCommandHandler> logger,
    IResearchStore store,
    IWikiTopics wikiTopics,
    WikiPaths wikiPaths,
    TimeProvider timeProvider)
{
    private static readonly HashSet<string> WikiWritingAgents = [KnownAgents.ThreadWeaver, KnownAgents.Editor];

    private static readonly HashSet<string> ChangingTools =
    [
        nameof(FileTools.CreateDirectory),
        nameof(FileTools.CreateFile),
        nameof(FileTools.PatchFile),
        nameof(FileTools.DeleteFile),
        nameof(FileTools.MoveFile)
    ];

    // How the run tracker joins the paths of a call that acts on two, such as a move's source and destination.
    private const string TargetSeparator = " → ";

    /// <summary>
    /// A run's changes persist whether it succeeded or failed, so both are recorded. A topic gone from the wiki after
    /// the run was deleted or moved away, and is pruned; its new location is a new topic. A new topic is curated
    /// straight away. Only the ThreadWeaver adds knowledge, so only its changes to a known topic ask for the topic's
    /// queries to be revisited; the Editor's restructuring does not, nor does any change to a topic whose brief and
    /// queries the author edited (pinned). A muted topic still collects its changes in a
    /// request, so its queries catch up once it is unmuted; the Pleb leaves muted topics' requests waiting.
    /// One topic that cannot be updated never stops the others; the failures are returned together.
    /// </summary>
    public async Task<Result<TopicChanges, Error>> Handle(
        RecordTopicChangesCommand request,
        CancellationToken cancellationToken)
    {
        AgentRun run = request.Run;
        if (!WikiWritingAgents.Contains(run.Agent))
        {
            return new TopicChanges([], [], []);
        }

        ILookup<string, string> changedPathsByTopic = run.ToolCalls
            .Where(call => call.Succeeded && call.Target is not null && ChangingTools.Contains(call.Tool))
            .SelectMany(call => call.Target!.Split(TargetSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(WikiRelative)
            .Choose()
            .Distinct(StringComparer.Ordinal)
            .Select(path => (Path: path, Topic: TopicPaths.TopicOf(path)))
            .Where(change => change.Topic.HasValue)
            .ToLookup(change => change.Topic.Value, change => change.Path, StringComparer.Ordinal);

        List<(string Topic, TopicOutcome Outcome)> outcomes = [];
        List<Error> errors = [];
        foreach (IGrouping<string, string> changedPaths in changedPathsByTopic)
        {
            Result<TopicOutcome, Error> recorded = await RecordAsync(changedPaths.Key, [.. changedPaths], run.Agent, cancellationToken);
            if (recorded.IsSuccess)
            {
                outcomes.Add((changedPaths.Key, recorded.Value));
            }
            else
            {
                errors.Add(recorded.Error);
            }
        }

        return errors.Count switch
        {
            0 => new TopicChanges(
                Topics(TopicOutcome.Created),
                Topics(TopicOutcome.Pruned),
                [.. outcomes.Where(outcome => outcome.Outcome is TopicOutcome.Created or TopicOutcome.CurationRequested).Select(outcome => outcome.Topic)]),
            1 => errors[0],
            _ => new AggregateError(errors)
        };

        string[] Topics(TopicOutcome kind) => [.. outcomes.Where(outcome => outcome.Outcome == kind).Select(outcome => outcome.Topic)];
    }

    private enum TopicOutcome
    {
        Unchanged,
        Created,
        Pruned,
        CurationRequested
    }

    /// <summary>A created topic is curated too, so <see cref="TopicOutcome.Created"/> includes a curation request.</summary>
    private async Task<Result<TopicOutcome, Error>> RecordAsync(
        string topic,
        IReadOnlyList<string> changedPaths,
        string agent,
        CancellationToken cancellationToken)
    {
        bool exists = wikiTopics.Exists(topic);
        bool wasKnown = false;
        Result<Maybe<ResearchTopic>, Error> updated = await store.UpdateTopicAsync(
            topic,
            current =>
            {
                wasKnown = current.HasValue;
                return exists
                    ? current.HasValue ? current : Maybe.From(ResearchTopic.New(topic, timeProvider.GetUtcNow()))
                    : Maybe<ResearchTopic>.None;
            },
            cancellationToken);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (!exists)
        {
            if (!wasKnown)
            {
                return TopicOutcome.Unchanged;
            }

            logger.PrunedTopic(topic);
            return TopicOutcome.Pruned;
        }

        // The author's own brief and queries stand until the author asks for a curation again.
        if (wasKnown && (agent != KnownAgents.ThreadWeaver || updated.Value.Value.Pinned))
        {
            return TopicOutcome.Unchanged;
        }

        return await store
            .RequestCurationAsync(new CurationRequest(topic, changedPaths, timeProvider.GetUtcNow()), cancellationToken)
            .Map(_ => wasKnown ? TopicOutcome.CurationRequested : TopicOutcome.Created)
            .Tap(outcome =>
            {
                if (outcome == TopicOutcome.Created)
                {
                    logger.DiscoveredTopic(topic);
                }
            });
    }

    /// <summary>
    /// The path as it is relative to the wiki root. Agents may name a page absolutely or with <c>..</c> segments, which
    /// the file tools accept as long as it stays inside the wiki.
    /// </summary>
    private Maybe<string> WikiRelative(string path) => wikiPaths.TryResolve(path, out string fullPath)
        ? Path.GetRelativePath(wikiPaths.Root, fullPath).Replace(Path.DirectorySeparatorChar, '/')
        : Maybe<string>.None;
}
