using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Research.Triage;

namespace KnowledgeBase.Agent.Tests.Research.Triage;

public sealed class KeeperAgreementTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Recent = Since.AddDays(2);
    private static readonly DateTimeOffset Old = Since.AddDays(-2);

    [Test]
    public void TheAuthorMarkedSomeVerdicts()
    {
        ResearchBatch batch = Batch(
            Result(Kept(Recent), ResultMark.Useful),
            Result(Kept(Recent), ResultMark.Noise),
            Result(Discarded(Recent), ResultMark.Noise),
            Result(Discarded(Recent), ResultMark.Useful),
            Result(Discarded(Recent), ResultMark.Noise));

        ResearchQuality quality = KeeperAgreement.From([batch], Since, "laya");

        ItShouldCountEveryMarkedVerdict();
        ItShouldCountTheVerdictsThatMatchTheirMarks();
        ItShouldCountTheNoiseItKept();
        ItShouldCountTheUsefulResultsItDiscarded();
        ItShouldNameTheKeepersModelAndWindow();

        void ItShouldCountEveryMarkedVerdict() => quality.Marked.ShouldBe(5);
        void ItShouldCountTheVerdictsThatMatchTheirMarks() => quality.Agreed.ShouldBe(3);
        void ItShouldCountTheNoiseItKept() => quality.NoiseKept.ShouldBe(1);
        void ItShouldCountTheUsefulResultsItDiscarded() => quality.UsefulDiscarded.ShouldBe(1);

        void ItShouldNameTheKeepersModelAndWindow() => quality.ShouldSatisfyAllConditions(
            figures => figures.KeeperModel.ShouldBe("laya"),
            figures => figures.WindowDays.ShouldBe(7));
    }

    [Test]
    public void SomeResultsSayNothingAboutTheKeeper()
    {
        ResearchBatch batch = Batch(
            Result(Kept(Old), ResultMark.Noise),
            Result(Verdict.Failed("The Keeper did not answer.", Recent), ResultMark.Noise),
            Result(Verdict.Discard("The page could not be fetched: 404.", Recent), ResultMark.Useful),
            Result(Kept(Recent), mark: null),
            Result(verdict: null, mark: null));

        ResearchQuality quality = KeeperAgreement.From([batch], Since, "laya");

        ItShouldCountNothing();

        void ItShouldCountNothing() => quality.ShouldSatisfyAllConditions(
            figures => figures.Marked.ShouldBe(0),
            figures => figures.Agreed.ShouldBe(0),
            figures => figures.NoiseKept.ShouldBe(0));
    }

    [Test]
    public void ResearchNotesWereResolved()
    {
        ResearchBatch[] batches =
        [
            Batch() with { SearchedAt = Old.AddHours(1), Outcome = IngestionOutcome.Positive, ResolvedAt = Recent },
            Batch() with { SearchedAt = Old.AddHours(2), Outcome = IngestionOutcome.Positive, ResolvedAt = Recent },
            Batch() with { SearchedAt = Old.AddHours(3), Outcome = IngestionOutcome.Empty, ResolvedAt = Recent },
            Batch() with { SearchedAt = Old.AddHours(4), Outcome = IngestionOutcome.Failed, ResolvedAt = Recent },
            Batch() with { SearchedAt = Old.AddHours(5), Outcome = IngestionOutcome.Positive, ResolvedAt = Old },
            Batch() with { SearchedAt = Old.AddHours(6) }
        ];

        ResearchQuality quality = KeeperAgreement.From(batches, Since, "laya");

        ItShouldCountEachOutcomeWithinTheWindow();

        void ItShouldCountEachOutcomeWithinTheWindow() => quality.ShouldSatisfyAllConditions(
            figures => figures.NotesFiled.ShouldBe(2),
            figures => figures.NotesEmpty.ShouldBe(1),
            figures => figures.NotesFailed.ShouldBe(1));
    }

    [Test]
    public void ACrashLeftABatchInTwoFolders()
    {
        ResearchBatch batch = Batch(Result(Kept(Recent), ResultMark.Useful)) with { Outcome = IngestionOutcome.Positive, ResolvedAt = Recent };

        ResearchQuality quality = KeeperAgreement.From([batch, batch], Since, "laya");

        ItShouldCountItOnce();

        void ItShouldCountItOnce() => quality.ShouldSatisfyAllConditions(
            figures => figures.Marked.ShouldBe(1),
            figures => figures.NotesFiled.ShouldBe(1));
    }

    private static Verdict Kept(DateTimeOffset judgedAt) => Verdict.Keep("A summary.", [], judgedAt) with { Useful = 0.8, Injection = 0.01 };

    private static Verdict Discarded(DateTimeOffset judgedAt) => Verdict.Discard("Off topic.", judgedAt) with { Useful = 0.2, Injection = 0.01 };

    private static BatchResult Result(Verdict? verdict, ResultMark? mark) =>
        new("raised beds", $"https://example.com/{Guid.NewGuid():N}", new SearchResult("https://example.com", "A page", null, "SearXNG", null))
        {
            Verdict = verdict,
            Mark = mark
        };

    private static ResearchBatch Batch(params BatchResult[] results) => new("projects/garden", Old, results);
}
