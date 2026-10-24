using KnowledgeBase.Agent.Review.Models;
using KnowledgeBase.Agent.Review.Queries;
using KnowledgeBase.Agent.Review.Tools;

namespace KnowledgeBase.Agent.IntegrationTests.Review;

public sealed class ListFindingsQueryHandlerTests : IAsyncDisposable
{
    private static readonly NewReviewFinding[] Findings =
    [
        new("people/susan.md", "No link back to the people index.", "Add the link.", PriorityLevel.Low),
        new("me/goals.md", "The goal has no date.", "Add the target date.", PriorityLevel.High)
    ];

    private readonly TestWiki _wiki = new();
    private readonly ListFindingsQueryHandler _handler;

    public ListFindingsQueryHandlerTests()
    {
        _handler = _wiki.Get<ListFindingsQueryHandler>();
    }

    public ValueTask DisposeAsync() => _wiki.DisposeAsync();

    [Test]
    public async Task ACorruptFindingIsAmongOpenFindings()
    {
        await _wiki.InitialiseAsync();
        ReviewWikiResult review = await _wiki.Get<ReviewTools>().WriteFindings("Mostly tidy.", Findings, CancellationToken.None);
        _wiki.Write(_wiki.Settings.ReviewOpenFindingsDirectory, "review-edited-by-hand-001.json", "{ \"id\": \"review-edi");

        Result<IReadOnlyCollection<ReviewFinding>, Error> open = await _handler
            .Handle(new ListFindingsQuery(FindingState.Open), CancellationToken.None);

        ItShouldStillListTheOpenFindings();
        ItShouldSkipTheCorruptFinding();

        void ItShouldStillListTheOpenFindings() => open.IsSuccess.ShouldBeTrue(open.IsFailure ? open.Error.ToString() : null);

        void ItShouldSkipTheCorruptFinding() =>
            open.Value.Select(finding => finding.Id).ShouldBe(review.FindingIds, ignoreOrder: true);
    }
}