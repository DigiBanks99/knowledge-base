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

        Result<IReadOnlyCollection<ReviewFinding>, Error> open = await List(FindingState.Open);

        ItShouldStillListTheOpenFindings();
        ItShouldSkipTheCorruptFinding();

        void ItShouldStillListTheOpenFindings() => open.IsSuccess.ShouldBeTrue(open.IsFailure ? open.Error.ToString() : null);

        void ItShouldSkipTheCorruptFinding() =>
            open.Value.Select(finding => finding.Id).ShouldBe(review.FindingIds, ignoreOrder: true);
    }

    [Test]
    public async Task FindingsAreListedWithoutAState()
    {
        await _wiki.InitialiseAsync();
        ReviewTools tools = _wiki.Get<ReviewTools>();
        ReviewWikiResult review = await tools.WriteFindings("Mostly tidy.", Findings, CancellationToken.None);
        string resolvedId = review.FindingIds.First();
        string openId = review.FindingIds.Last();
        await tools.ResolveFindings([new FindingResolution(resolvedId, "Added the link.")], CancellationToken.None);

        Result<IReadOnlyCollection<ReviewFinding>, Error> all = await List(state: null);
        Result<IReadOnlyCollection<ReviewFinding>, Error> open = await List(FindingState.Open);
        Result<IReadOnlyCollection<ReviewFinding>, Error> closed = await List(FindingState.Closed);

        ItShouldListOpenAndClosedFindings();
        ItShouldStillFilterByState();

        void ItShouldListOpenAndClosedFindings() =>
            all.Value.Select(finding => finding.Id).ShouldBe([resolvedId, openId], ignoreOrder: true);

        void ItShouldStillFilterByState()
        {
            open.Value.Select(finding => finding.Id).ShouldBe([openId]);
            closed.Value.Select(finding => finding.Id).ShouldBe([resolvedId]);
        }
    }

    [Test]
    public async Task AnUnknownStateIsRequested()
    {
        await _wiki.InitialiseAsync();

        Result<IReadOnlyCollection<ReviewFinding>, Error> listed = await List((FindingState)42);

        ItShouldRejectTheState();

        void ItShouldRejectTheState() => listed.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("UnknownFindingState");
    }

    private Task<Result<IReadOnlyCollection<ReviewFinding>, Error>> List(FindingState? state) =>
        _handler.Handle(new ListFindingsQuery(state), CancellationToken.None);
}
