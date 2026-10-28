using KnowledgeBase.Agent.Research.Curation;
using KnowledgeBase.Agent.Research.Fetch;

namespace KnowledgeBase.Agent.Tests.Research.Fetch;

public sealed class ContentBudgetTests
{
    private readonly IPromptTokenCounter _counter = Substitute.For<IPromptTokenCounter>();
    private readonly ContentBudget _budget;

    public ContentBudgetTests()
    {
        // One token a word, so the tests can reason about where a cut lands.
        _counter.CountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<int, Error>(Words(call.Arg<string>())));
        _budget = new ContentBudget(_counter);
    }

    [Test]
    public async Task ContentFitsTheBudget()
    {
        FittedContent fitted = await _budget.FitAsync("A short article.", 10, CancellationToken.None);

        ItShouldKeepItWhole();

        void ItShouldKeepItWhole() => fitted.ShouldBe(new FittedContent("A short article.", false));
    }

    [Test]
    public async Task ContentIsOverTheBudget()
    {
        string text = string.Join("\n\n", Enumerable.Range(1, 40).Select(paragraph => $"Paragraph {paragraph} has a few words in it."));

        FittedContent fitted = await _budget.FitAsync(text, 100, CancellationToken.None);

        ItShouldCutItToTheBudget();
        ItShouldCutAtAParagraph();
        ItShouldSayItWasCut();

        void ItShouldCutItToTheBudget() => Words(fitted.Text).ShouldBeInRange(50, 100);

        void ItShouldCutAtAParagraph() => fitted.Text.ShouldEndWith("words in it.");

        void ItShouldSayItWasCut() => fitted.Truncated.ShouldBeTrue();
    }

    [Test]
    public async Task TheModelServerCannotCount()
    {
        _counter.CountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<int, Error>(new TokenCountUnavailableError("connection refused")));
        string text = new('a', 3000);

        FittedContent fitted = await _budget.FitAsync(text, 100, CancellationToken.None);

        ItShouldCutItByTheEstimate();

        // The estimate counts three characters a token.
        void ItShouldCutItByTheEstimate() => fitted.ShouldSatisfyAllConditions(
            () => fitted.Text.Length.ShouldBeLessThanOrEqualTo(300),
            () => fitted.Truncated.ShouldBeTrue());
    }

    private static int Words(string text) => text.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
}
