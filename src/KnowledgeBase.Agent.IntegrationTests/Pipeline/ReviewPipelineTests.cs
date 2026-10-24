using System.Text.Json;

using KnowledgeBase.Agent.Review.Commands;
using KnowledgeBase.Agent.Review.Models;
using KnowledgeBase.Agent.Review.Tools;

namespace KnowledgeBase.Agent.IntegrationTests.Pipeline;

/// <summary>
/// A review request, the Reviewer's findings and the Editor resolving them, with the agents scripted.
/// </summary>
public sealed class ReviewPipelineTests : IAsyncDisposable
{
    private readonly TestWiki _wiki = new();

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task AReviewIsRequested()
    {
        NewReviewFinding[] findings =
        [
            new("people/susan.md", "No link back to the people index.", "Add the link.", PriorityLevel.Low),
            new("projects/index.md", "Lists a finished project as active.", "Move it to done.", PriorityLevel.Medium),
            new("me/goals.md", "The goal has no date.", "Add the target date.", PriorityLevel.High)
        ];
        ReviewTools reviewTools = _wiki.Get<ReviewTools>();
        _wiki.Reviewer.Behaviour = async (_, cancellationToken) =>
        {
            ReviewWikiResult result = await _wiki.CallToolAsync(
                nameof(ReviewTools.WriteFindings),
                null,
                () => reviewTools.WriteFindings("Mostly tidy.", findings, cancellationToken));
            return result.Summary;
        };
        _wiki.Editor.Behaviour = async (_, cancellationToken) =>
        {
            IReadOnlyCollection<ReviewFinding> open = await reviewTools.ListFindings(cancellationToken);
            IReadOnlyCollection<string> resolved = await _wiki.CallToolAsync(
                nameof(ReviewTools.ResolveFindings),
                null,
                () => reviewTools.ResolveFindings([.. open.Select(finding => new FindingResolution(finding.Id, "Fixed."))], cancellationToken));
            return $"Resolved {resolved.Count} findings.";
        };
        await _wiki.InitialiseAsync();
        _wiki.Start();

        Result<string, Error> requested = await _wiki.Get<RequestReviewWikiCommandHandler>()
            .Handle(new RequestReviewWikiCommand(DateTimeOffset.Parse("2026-10-24T10:00:00Z")), CancellationToken.None);
        // Waiting for the outcome rather than a number of runs lets an extra Editor run fail its own check below instead
        // of timing out.
        bool settled = await Eventually.Until(
            async () => TestWiki.FileNames(_wiki.Settings.ReviewClosedFindingsDirectory).Count == findings.Length
                && TestWiki.FileNames(_wiki.Settings.CompletedReviewRequestDirectory).Count == 1
                && await _wiki.RunsFinishedAsync(),
            TimeSpan.FromSeconds(30));
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();
        IReadOnlyList<ReviewFinding> closed = TestWiki
            .FileNames(_wiki.Settings.ReviewClosedFindingsDirectory)
            .Select(name => JsonSerializer.Deserialize<ReviewFinding>(
                File.ReadAllText(Path.Combine(_wiki.Settings.ReviewClosedFindingsDirectory, name)),
                JsonSerializerOptions.Web)!)
            .ToList();

        ItShouldAcceptTheRequest();
        ItShouldFinish();
        ItShouldCompleteTheRequest();
        ItShouldWriteAReport();
        ItShouldCloseEveryFinding();
        ItShouldRunTheEditorOnceForAllTheFindings();

        void ItShouldAcceptTheRequest() => requested.IsSuccess.ShouldBeTrue();

        void ItShouldFinish() => settled.ShouldBeTrue(pipeline);

        void ItShouldCompleteTheRequest()
        {
            TestWiki.FileNames(_wiki.Settings.CompletedReviewRequestDirectory).ShouldHaveSingleItem();
            TestWiki.FileNames(_wiki.Settings.ReviewRequestDirectory).ShouldBeEmpty();
        }

        void ItShouldWriteAReport() => TestWiki.FileNames(_wiki.Settings.ReviewReportsDirectory).ShouldHaveSingleItem();

        void ItShouldCloseEveryFinding()
        {
            TestWiki.FileNames(_wiki.Settings.ReviewOpenFindingsDirectory).ShouldBeEmpty();
            closed.Select(finding => finding.PagePath).ShouldBe(findings.Select(finding => finding.PagePath), ignoreOrder: true);
            closed.ShouldAllBe(finding => finding.Resolution == "Fixed." && finding.ResolvedAt != null);
        }

        void ItShouldRunTheEditorOnceForAllTheFindings() => _wiki.Editor.Prompts.ShouldHaveSingleItem();
    }

    [Test]
    public async Task AReviewIsRequestedByHand()
    {
        await _wiki.InitialiseAsync();
        _wiki.Start();

        _wiki.Write(_wiki.Settings.ReviewRequestDirectory, "please-review.md", "Please review the people pages.");
        bool settled = await Eventually.Until(async () =>
            TestWiki.FileNames(_wiki.Settings.CompletedReviewRequestDirectory).Count == 1 && await _wiki.RunsFinishedAsync(1));
        string pipeline = settled ? string.Empty : await _wiki.DescribeAsync();

        ItShouldStillReviewTheWiki();
        ItShouldCompleteTheRequest();

        void ItShouldStillReviewTheWiki() => _wiki.Reviewer.Prompts.ShouldHaveSingleItem();

        void ItShouldCompleteTheRequest()
        {
            settled.ShouldBeTrue(pipeline);
            TestWiki.FileNames(_wiki.Settings.CompletedReviewRequestDirectory).ShouldBe(["please-review.md"]);
            TestWiki.FileNames(_wiki.Settings.ReviewRequestDirectory).ShouldBeEmpty();
        }
    }
}