namespace KnowledgeBase.Agent.Tests;

public sealed class ErrorTests
{
    private readonly ValidationError _first = new("First", "The first error.");
    private readonly ValidationError _second = new("Second", "The second error.");

    [Test]
    public void TwoErrorsAreCombined()
    {
        ICombine combined = _first.Combine(_second);

        ItShouldAggregateTheErrors();
        ItShouldKeepTheErrorsInOrder();

        void ItShouldAggregateTheErrors() => combined.ShouldBeOfType<AggregateError>();

        void ItShouldKeepTheErrorsInOrder() =>
            ((AggregateError)combined).Errors.ShouldBe([_first, _second]);
    }

    [Test]
    public void AnErrorIsCombinedWithAnAggregate()
    {
        ValidationError third = new("Third", "The third error.");
        AggregateError aggregate = new([_first, _second]);

        ICombine combined = third.Combine(aggregate);

        ItShouldAppendTheErrorToTheAggregatedErrors();

        void ItShouldAppendTheErrorToTheAggregatedErrors() =>
            combined.ShouldBeOfType<AggregateError>().Errors.ShouldBe([_first, _second, third]);
    }

    [Test]
    public void AnErrorIsCombinedWithSomethingElse()
    {
        ICombine other = Substitute.For<ICombine>();

        ICombine combined = _first.Combine(other);

        ItShouldKeepTheOriginalError();

        void ItShouldKeepTheOriginalError() => combined.ShouldBeSameAs(_first);
    }

    [Test]
    public void AnErrorIsFormatted()
    {
        string formatted = _first.ToString();

        ItShouldShowTheCodeAndMessage();

        void ItShouldShowTheCodeAndMessage() => formatted.ShouldBe("First: The first error.");
    }
}