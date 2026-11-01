using KnowledgeBase.Agent.Research.Triage;

namespace KnowledgeBase.Agent.Tests.Research.Triage;

public sealed class KeeperPolicyTests
{
    private const double Keep = 0.6;
    private const double Injection = 0.9;

    private static readonly string[] Paragraphs =
    [
        "Atlas 2 ships a new scheduler and drops the old plugin API.",
        "Upgrading takes an afternoon for most projects.",
        "The release notes list every breaking change.",
        "Benchmarks show the scheduler is twice as fast."
    ];

    [Test]
    [Arguments(0.6)]
    [Arguments(0.95)]
    public void AResultIsUsefulEnough(double useful)
    {
        KeeperDecision decision = KeeperPolicy.Judge(new Screening(useful, 0.5), Keep, Injection);

        ItShouldBeKept();

        void ItShouldBeKept() => decision.ShouldBe(new KeeperDecision(true, null));
    }

    [Test]
    public void AResultIsNotUsefulEnough()
    {
        KeeperDecision decision = KeeperPolicy.Judge(new Screening(0.42, 0.1), Keep, Injection);

        ItShouldBeDiscardedSayingHowUsefulItWas();

        void ItShouldBeDiscardedSayingHowUsefulItWas() =>
            decision.ShouldBe(new KeeperDecision(false, "Not useful enough for the topic (useful 0.42, below 0.60)."));
    }

    [Test]
    [Arguments(0.9)]
    [Arguments(0.99)]
    public void AUsefulResultLooksLikeAnInjection(double injection)
    {
        KeeperDecision decision = KeeperPolicy.Judge(new Screening(0.95, injection), Keep, Injection);

        ItShouldBeDiscardedAsAnInjection();

        void ItShouldBeDiscardedAsAnInjection() => decision.ShouldSatisfyAllConditions(
            () => decision.Kept.ShouldBeFalse(),
            () => decision.Reason!.ShouldStartWith("Looks like instructions aimed at an AI (injection "));
    }

    [Test]
    public void AUselessResultLooksLikeAnInjection()
    {
        KeeperDecision decision = KeeperPolicy.Judge(new Screening(0.1, 0.93), Keep, Injection);

        ItShouldGiveTheInjectionAsTheReason();

        void ItShouldGiveTheInjectionAsTheReason() =>
            decision.Reason.ShouldBe("Looks like instructions aimed at an AI (injection 0.93, at or above 0.90).");
    }

    [Test]
    public void TheKeeperRanksEveryParagraph()
    {
        Dictionary<int, double> probabilities = new() { [1] = 0.05, [2] = 0.6, [3] = 0.2, [4] = 0.15 };

        IReadOnlyList<string> quotes = KeeperPolicy.Quotes(probabilities, Paragraphs, 0.1);

        ItShouldTakeTheThreeLikeliestInOrder();

        void ItShouldTakeTheThreeLikeliestInOrder() => quotes.ShouldBe([Paragraphs[1], Paragraphs[2], Paragraphs[3]]);
    }

    [Test]
    public void OnlyOneParagraphIsLikely()
    {
        Dictionary<int, double> probabilities = new() { [1] = 0.97, [2] = 0.02, [3] = 0.01 };

        IReadOnlyList<string> quotes = KeeperPolicy.Quotes(probabilities, Paragraphs, 0.1);

        ItShouldDropTheQuotesBelowTheFloor();

        void ItShouldDropTheQuotesBelowTheFloor() => quotes.ShouldBe([Paragraphs[0]]);
    }

    [Test]
    public void AQuotedParagraphIsLong()
    {
        string[] paragraphs = [new string('a', 200) + " " + new string('b', 200)];

        IReadOnlyList<string> quotes = KeeperPolicy.Quotes(new Dictionary<int, double> { [1] = 0.9 }, paragraphs, 0.1);

        ItShouldCapItsLength();

        void ItShouldCapItsLength() => quotes.Single().ShouldSatisfyAllConditions(
            quote => quote.Length.ShouldBe(KeeperPolicy.MaxQuoteLength),
            quote => quote.ShouldEndWith("…"));
    }

    [Test]
    public void TheAnswerNamesAParagraphThatWasNotOffered()
    {
        IReadOnlyList<string> quotes = KeeperPolicy.Quotes(new Dictionary<int, double> { [9] = 0.9, [0] = 0.8 }, Paragraphs, 0.1);

        ItShouldIgnoreIt();

        void ItShouldIgnoreIt() => quotes.ShouldBeEmpty();
    }

    [Test]
    public void ADescriptionIsSummarised()
    {
        Description description = new("release", 0.81, new Dictionary<int, double> { [3] = 0.9, [1] = 0.3, [2] = 0.01 });

        string summary = KeeperPolicy.Summarise(description, Paragraphs.Length, 0.1);

        ItShouldNameTheKindAndTheQuotedParagraphs();

        void ItShouldNameTheKindAndTheQuotedParagraphs() => summary.ShouldBe("Kind release (0.81); quotes 3, 1.");
    }
}
