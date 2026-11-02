using System.ComponentModel;

using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBase.Agent.Diagnostics;

internal static class LogsEndpoints
{
    private const int DefaultLimit = 200;
    private const int MaxLimit = 1000;

    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapLogsEndpoints()
        {
            endpoints
                .MapGet("logs", ListLogs)
                .WithTags("logs")
                .WithName(nameof(ListLogs))
                .WithSummary("List recent log entries")
                .WithDescription("Returns the most recent log entries, newest first. The API keeps the latest few thousand in memory; the logs directory keeps the last five days.")
                .Produces<IReadOnlyList<LogEntry>>();

            return endpoints;
        }
    }

    private static IReadOnlyList<LogEntry> ListLogs(
        [FromQuery, Description("The lowest level to return, e.g. Warning. Defaults to Trace, which returns everything kept.")] LogLevel? level,
        [FromQuery, Description("Only entries from this part of the app, e.g. Research, Review or Runs.")] string? area,
        [FromQuery, Description("Only entries whose message, category, exception or logged values (such as a topic or run id) contain this text, ignoring case.")] string? search,
        [FromQuery, Description("The maximum number of entries to return.")] int? limit,
        LogBuffer buffer) =>
        buffer.Query(
            level ?? LogLevel.Trace,
            string.IsNullOrWhiteSpace(area) ? null : area.Trim(),
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit));
}
