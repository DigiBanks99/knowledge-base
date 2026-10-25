using System.Net;
using System.Net.Http.Json;

using KnowledgeBase.Agent.Review.Commands;
using KnowledgeBase.Agent.Review.Endpoints;
using KnowledgeBase.Agent.Review.Models;
using KnowledgeBase.Agent.Review.Tools;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

public sealed class ReviewApiTests : IDisposable
{
    private readonly TestApi _api = new();

    public void Dispose() => _api.Dispose();

    [Test]
    public async Task FindingsAreRequested()
    {
        HttpClient client = _api.Client();
        (string resolvedId, string openId) = await RecordAndResolveOneOfTwoFindingsAsync();

        IReadOnlyList<ReviewFinding> all = await ListAsync(client, "/api/review");
        IReadOnlyList<ReviewFinding> open = await ListAsync(client, "/api/review?state=Open");
        IReadOnlyList<ReviewFinding> closed = await ListAsync(client, "/api/review?state=Closed");

        ItShouldListEveryFindingWithoutAState();
        ItShouldFilterByState();

        void ItShouldListEveryFindingWithoutAState() =>
            all.Select(finding => finding.Id).ShouldBe([resolvedId, openId], ignoreOrder: true);

        void ItShouldFilterByState()
        {
            open.Select(finding => finding.Id).ShouldBe([openId]);
            closed.Select(finding => finding.Id).ShouldBe([resolvedId]);
        }
    }

    [Test]
    [Arguments("Bogus")]
    [Arguments("42")]
    public async Task FindingsInAnUnknownStateAreRequested(string state)
    {
        HttpResponseMessage response = await _api.Client().GetAsync($"/api/review?state={state}");

        ItShouldRefuse();

        void ItShouldRefuse() => response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AReviewIsRequested()
    {
        HttpResponseMessage response = await _api.Client().PostAsJsonAsync(
            "/api/review",
            new RequestReviewWikiCommand(DateTimeOffset.Parse("2026-10-24T10:00:00Z")),
            TestApi.Json);
        bool reviewed = await Eventually.Until(() => _api.Agent(KnownAgents.Reviewer).Prompts.Count == 1);

        ItShouldRecordTheRequest();
        ItShouldStartTheReviewer();

        void ItShouldRecordTheRequest() => response.StatusCode.ShouldBe(HttpStatusCode.Created);

        void ItShouldStartTheReviewer() => reviewed.ShouldBeTrue();
    }

    [Test]
    public async Task ReportsAreRequested()
    {
        HttpClient client = _api.Client();
        await RecordAndResolveOneOfTwoFindingsAsync();

        IReadOnlyList<ReviewReport>? reports = await client.GetFromJsonAsync<IReadOnlyList<ReviewReport>>("/api/review/reports", TestApi.Json);

        ItShouldSummariseTheReview();

        void ItShouldSummariseTheReview() => reports.ShouldNotBeNull().ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            report => report.Summary.ShouldBe("Mostly tidy."),
            report => report.FindingIds.Count.ShouldBe(2));
    }

    private async Task<(string ResolvedId, string OpenId)> RecordAndResolveOneOfTwoFindingsAsync()
    {
        ReviewTools tools = _api.Get<ReviewTools>();
        ReviewWikiResult review = await tools.WriteFindings(
            "Mostly tidy.",
            [
                new NewReviewFinding("people/susan.md", "No link back to the people index.", "Add the link.", PriorityLevel.Low),
                new NewReviewFinding("me/goals.md", "The goal has no date.", "Add the target date.", PriorityLevel.High)
            ],
            CancellationToken.None);
        string resolvedId = review.FindingIds.First();
        await tools.ResolveFindings([new FindingResolution(resolvedId, "Added the link.")], CancellationToken.None);
        return (resolvedId, review.FindingIds.Last());
    }

    private static async Task<IReadOnlyList<ReviewFinding>> ListAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<IReadOnlyList<ReviewFinding>>(url, TestApi.Json))!;
}
