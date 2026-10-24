using System.Text.Json;

using KnowledgeBase.Agent.Review.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Review.Endpoints;

public sealed record ReviewReport(string Id, DateTimeOffset ReviewDate, string Summary, IReadOnlyCollection<string> FindingIds);

internal static class ListReports
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapListReports()
        {
            endpoints
                .MapGet("reports", Handle)
                .WithName(nameof(ListReports))
                .WithSummary("List review reports")
                .WithDescription("Returns the summary of every completed review, newest first.")
                .Produces<IReadOnlyList<ReviewReport>>();

            return endpoints;
        }
    }

    private static async Task<IReadOnlyList<ReviewReport>> Handle(
        IOptions<WikiConfigurationSettings> settings,
        CancellationToken cancellationToken)
    {
        List<ReviewReport> reports = [];
        foreach (string filePath in Directory.EnumerateFiles(settings.Value.ReviewReportsDirectory, "*.json"))
        {
            try
            {
                await using FileStream stream = File.OpenRead(filePath);
                ReviewWikiResult? result = await JsonSerializer.DeserializeAsync<ReviewWikiResult>(stream, JsonSerializerOptions.Web, cancellationToken);
                if (result is not null)
                {
                    reports.Add(new ReviewReport(Path.GetFileNameWithoutExtension(filePath), result.ReviewDate, result.Summary, result.FindingIds));
                }
            }
            catch (JsonException)
            {
                // Skip reports in an older or unreadable format.
            }
        }

        return reports.OrderByDescending(report => report.ReviewDate).ToList();
    }
}