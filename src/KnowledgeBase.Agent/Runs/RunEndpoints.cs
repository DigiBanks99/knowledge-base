using System.ComponentModel;

using KnowledgeBase.Agent.Runs.Models;

using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBase.Agent.Runs;

internal static class RunEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapRunEndpoints()
        {
            RouteGroupBuilder runs = endpoints
                .MapGroup("runs")
                .WithTags("runs");

            runs.MapGet(string.Empty, ListRuns)
                .WithName(nameof(ListRuns))
                .WithSummary("List agent runs")
                .WithDescription("Returns the most recent agent runs, newest first.")
                .Produces<IReadOnlyList<AgentRun>>();

            runs.MapGet("{id}", GetRun)
                .WithName(nameof(GetRun))
                .WithSummary("Get an agent run")
                .Produces<AgentRun>()
                .Produces(StatusCodes.Status404NotFound);

            return endpoints;
        }
    }

    private static async Task<IReadOnlyList<AgentRun>> ListRuns(
        [FromQuery, Description("Only runs of this agent, e.g. FactFinder.")] string? agent,
        [FromQuery, Description("Only runs started by this kind of trigger.")] AgentRunTriggerKind? triggerKind,
        [FromQuery, Description("Only runs started for this file, e.g. a note name.")] string? trigger,
        [FromQuery, Description("The maximum number of runs to return.")] int? limit,
        AgentRunStore store,
        CancellationToken cancellationToken) =>
        await store.ListAsync(agent, triggerKind, trigger, Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit), cancellationToken);

    private static async Task<Microsoft.AspNetCore.Http.IResult> GetRun(
        string id,
        AgentRunStore store,
        CancellationToken cancellationToken) =>
        await store.GetAsync(id, cancellationToken) is { } run
            ? Results.Ok(run)
            : Results.NotFound();
}
