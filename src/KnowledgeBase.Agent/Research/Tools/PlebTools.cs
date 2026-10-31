using System.ComponentModel;

using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Runs;

namespace KnowledgeBase.Agent.Research.Tools;

/// <summary>
/// The Pleb's only tools: one to give a topic a new brief and queries, one to keep them and one to ask the author
/// when it cannot tell what the topic is about. A curation run is for one
/// topic, so a call naming another is refused.
/// </summary>
internal sealed class PlebTools(
    AgentRunTracker runTracker,
    SetTopicQueriesCommandHandler setQueriesHandler,
    KeepTopicQueriesCommandHandler keepQueriesHandler,
    RequestTopicReviewCommandHandler requestReviewHandler)
{
    // The limits are spelled out because attribute arguments cannot interpolate the TopicQueries constants.
    [Description("""
                 Gives the topic a new brief and new search queries. Call this when the topic has no queries yet, or when the changed pages make the current queries miss what the topic is now about.
                 The brief is one to three sentences (at most 500 characters) describing what is worth finding for the topic. Give 1 to 3 queries, each at most 100 characters.
                 Never put the name of a person or organisation in the brief or a query: the call is refused and you must rewrite them without it.
                 """)]
    public async Task<string> SetTopicQueries(
        [Description("The topic's path exactly as the prompt gives it, for example `projects/example`.")] string topic,
        [Description("One to three sentences describing what is worth finding for the topic, without names of people or organisations.")] string brief,
        [Description("1 to 3 web search queries for news about the topic.")] string[] queries,
        CancellationToken cancellationToken)
    {
        EnsureIsTheRunsTopic(topic);
        Result<ResearchTopic, Error> result = await setQueriesHandler
            .Handle(new SetTopicQueriesCommand(topic, brief, queries), cancellationToken);
        return result.IsFailure
            ? throw new InvalidOperationException(result.Error.Message)
            : $"Saved the brief and {result.Value.Queries.Count} queries for {topic}. You are done.";
    }

    [Description("""
                 Keeps the topic's current brief and queries. Call this when nothing in the changed pages changes what is worth searching for, so the queries stay comparable between searches.
                 Only a topic that already has queries can keep them.
                 """)]
    public async Task<string> KeepQueries(
        [Description("The topic's path exactly as the prompt gives it, for example `projects/example`.")] string topic,
        CancellationToken cancellationToken)
    {
        EnsureIsTheRunsTopic(topic);
        Result<ResearchTopic, Error> result = await keepQueriesHandler
            .Handle(new KeepTopicQueriesCommand(topic), cancellationToken);
        return result.IsFailure
            ? throw new InvalidOperationException(result.Error.Message)
            : $"Kept the queries for {topic}. You are done.";
    }

    [Description("""
                 Asks the author what the topic is about, instead of setting or keeping its queries. Call this only when you cannot name the subject unambiguously: a name or acronym with several plausible meanings, or a page that does not say what kind of thing the subject is or who makes it.
                 The topic is not searched until the author answers; you are then asked to curate it again with the answer. Do not also call `SetTopicQueries` or `KeepQueries` in this run.
                 """)]
    public async Task<string> RequestHumanReview(
        [Description("The topic's path exactly as the prompt gives it, for example `projects/example`.")] string topic,
        [Description("One or two sentences (at most 500 characters) saying what is unclear, for example which meanings of an acronym you could not choose between.")] string question,
        CancellationToken cancellationToken)
    {
        EnsureIsTheRunsTopic(topic);
        Result<ResearchTopic, Error> result = await requestReviewHandler
            .Handle(new RequestTopicReviewCommand(topic, question), cancellationToken);
        return result.IsFailure
            ? throw new InvalidOperationException(result.Error.Message)
            : $"Asked the author about {topic}. You are done.";
    }

    private void EnsureIsTheRunsTopic(string topic)
    {
        string? curating = runTracker.Current?.Topic;
        if (curating is not null && !string.Equals(topic, curating, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"This curation is for topic `{curating}`, not `{topic}`. Call the tool again with `topic` set to `{curating}`.");
        }
    }
}
