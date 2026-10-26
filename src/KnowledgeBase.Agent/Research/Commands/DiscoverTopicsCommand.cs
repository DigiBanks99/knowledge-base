using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Research.Commands;

/// <summary>
/// Adopts every topic in the wiki that has no research state yet: all of them on the first start after research was
/// added, and afterwards any a previous start failed to adopt or the author added by hand. Topics added by agent runs
/// are discovered as the runs end.
/// </summary>
public record struct DiscoverTopicsCommand;

internal sealed class DiscoverTopicsCommandHandler(
    ILogger<DiscoverTopicsCommandHandler> logger,
    IResearchStore store,
    IWikiTopics wikiTopics,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Every adopted topic is queued for curation; the Pleb drains the queue a few at a time, so adopting research
    /// doesn't saturate the model server. One topic that cannot be adopted never stops the others, and is retried on
    /// the next start. Returns the topics adopted.
    /// </summary>
    public async Task<Result<IReadOnlyList<string>, Error>> Handle(
        DiscoverTopicsCommand _,
        CancellationToken cancellationToken) => await store
        .ListTopicsAsync(cancellationToken)
        .Bind(known => AdoptAsync(
            [.. wikiTopics.All().Except(known.Select(topic => topic.Path), StringComparer.Ordinal)],
            cancellationToken))
        .TapError(logger.FailedToDiscoverTopics);

    private async Task<Result<IReadOnlyList<string>, Error>> AdoptAsync(
        IReadOnlyList<string> topics,
        CancellationToken cancellationToken)
    {
        if (topics.Count > 0)
        {
            logger.DiscoveringTopics(topics.Count);
        }

        List<string> adopted = [];
        List<Error> errors = [];
        foreach (string topic in topics)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            Result<CurationRequest, Error> result = await store
                .UpdateTopicAsync(
                    topic,
                    current => current.HasValue ? current : Maybe.From(ResearchTopic.New(topic, now)),
                    cancellationToken)
                .Bind(_ => store.RequestCurationAsync(new CurationRequest(topic, [], now), cancellationToken));
            if (result.IsSuccess)
            {
                adopted.Add(topic);
            }
            else
            {
                errors.Add(result.Error);
            }
        }

        return errors.Count switch
        {
            0 => adopted,
            1 => errors[0],
            _ => new AggregateError(errors)
        };
    }
}
