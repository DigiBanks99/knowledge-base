using System.Text.Json;

using KnowledgeBase.Agent.Review.Models;
using KnowledgeBase.Agent.Review.Tools;

namespace KnowledgeBase.Agent.IntegrationTests.Review;

public sealed class ReviewToolsTests : IAsyncDisposable
{
    private static readonly NewReviewFinding[] Findings =
    [
        new("people/susan.md", "No link back to the people index.", "Add the link.", PriorityLevel.Low),
        new("me/goals.md", "The goal has no date.", "Add the target date.", PriorityLevel.High)
    ];

    private readonly TestWiki _wiki = new();
    private readonly ReviewTools _tools;

    public ReviewToolsTests()
    {
        _tools = _wiki.Get<ReviewTools>();
    }

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task AReviewIsRecorded()
    {
        await _wiki.InitialiseAsync();

        ReviewWikiResult result = await _tools.WriteFindings("Mostly tidy.", Findings, CancellationToken.None);
        string reviewId = result.FindingIds.First()[..^4];

        ItShouldNumberTheFindingsWithinTheReview();
        ItShouldOpenEachFinding();
        ItShouldWriteAReport();

        void ItShouldNumberTheFindingsWithinTheReview() =>
            result.FindingIds.ShouldBe([$"{reviewId}-001", $"{reviewId}-002"]);

        void ItShouldOpenEachFinding() => TestWiki.FileNames(_wiki.Settings.ReviewOpenFindingsDirectory)
            .ShouldBe(result.FindingIds.Select(id => $"{id}.json"));

        void ItShouldWriteAReport()
        {
            ReviewWikiResult report = Read<ReviewWikiResult>(_wiki.Settings.ReviewReportsDirectory, $"{reviewId}.json");
            report.Summary.ShouldBe("Mostly tidy.");
            report.FindingIds.ShouldBe(result.FindingIds);
        }
    }

    [Test]
    public async Task AFindingIsResolved()
    {
        await _wiki.InitialiseAsync();
        ReviewWikiResult review = await _tools.WriteFindings("Mostly tidy.", Findings, CancellationToken.None);
        string resolvedId = review.FindingIds.First();
        string openId = review.FindingIds.Last();

        IReadOnlyCollection<string> resolved = await _tools.ResolveFindings(
            [new FindingResolution(resolvedId, "Added the link.")],
            CancellationToken.None);

        ItShouldReportTheResolvedFinding();
        ItShouldCloseItWithTheResolution();
        ItShouldLeaveTheOtherFindingOpen();

        void ItShouldReportTheResolvedFinding() => resolved.ShouldBe([resolvedId]);

        void ItShouldCloseItWithTheResolution() =>
            Read<ReviewFinding>(_wiki.Settings.ReviewClosedFindingsDirectory, $"{resolvedId}.json").ShouldSatisfyAllConditions(
                finding => finding.Resolution.ShouldBe("Added the link."),
                finding => finding.ResolvedAt.ShouldNotBeNull(),
                finding => finding.PagePath.ShouldBe("people/susan.md"));

        void ItShouldLeaveTheOtherFindingOpen() =>
            TestWiki.FileNames(_wiki.Settings.ReviewOpenFindingsDirectory).ShouldBe([$"{openId}.json"]);
    }

    [Test]
    public async Task AnUnknownFindingIsResolved()
    {
        await _wiki.InitialiseAsync();
        ReviewWikiResult review = await _tools.WriteFindings("Mostly tidy.", Findings, CancellationToken.None);
        string knownId = review.FindingIds.First();

        Exception? exception = await Catch.ExceptionAsync(() => _tools.ResolveFindings(
            [new FindingResolution(knownId, "Added the link."), new FindingResolution("review-made-up-001", "Fixed.")],
            CancellationToken.None));

        ItShouldNameTheUnknownFinding();
        ItShouldStillResolveTheKnownFinding();

        void ItShouldNameTheUnknownFinding() => exception
            .ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldBe("Resolved 1 findings, but these ids are not open findings: review-made-up-001");

        void ItShouldStillResolveTheKnownFinding() =>
            TestWiki.FileNames(_wiki.Settings.ReviewClosedFindingsDirectory).ShouldBe([$"{knownId}.json"]);
    }

    private static T Read<T>(string directory, string name) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(directory, name)), JsonSerializerOptions.Web)!;
}