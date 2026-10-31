using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class RejectTopicCurationCommandHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Okf = "learning/okf.md";

    private readonly ResearchTopics _state = new();
    private readonly RejectTopicCurationCommandHandler _handler;

    public RejectTopicCurationCommandHandlerTests()
    {
        _state.Topics[Okf] = ResearchTopic.New(Okf, Discovered) with
        {
            Brief = "News about OKF.",
            Queries = ["okf", "okf news"],
            Pinned = true,
            ApprovedAt = Discovered
        };
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _handler = new RejectTopicCurationCommandHandler(
            NullLogger<RejectTopicCurationCommandHandler>.Instance,
            Options.Create(new ResearchSettings { GuidanceLimit = 2 }),
            FakeResearchStore.Holding(_state),
            new WikiPaths(Options.Create(new WikiConfigurationSettings { RootDirectory = "/data/kb" })),
            timeProvider);
    }

    [Test]
    public async Task TheAuthorRejectsACurationWithAReason()
    {
        await _handler.Handle(new RejectTopicCurationCommand("learning--okf.md", "OKF is a data format, not the HN project."), CancellationToken.None);

        ItShouldKeepTheReasonWithWhatWasRejected();
        ItShouldQueueACurationForTheAuthor();
        ItShouldWithdrawTheApprovalAndThePin();

        void ItShouldKeepTheReasonWithWhatWasRejected() => _state.Topics[Okf].Guidance.ShouldNotBeNull().ShouldHaveSingleItem()
            .ShouldSatisfyAllConditions(
                entry => entry.Kind.ShouldBe(GuidanceKind.Rejection),
                entry => entry.Text.ShouldBe("OKF is a data format, not the HN project."),
                entry => entry.RejectedBrief.ShouldBe("News about OKF."),
                entry => entry.RejectedQueries.ShouldBe(["okf", "okf news"]));

        void ItShouldQueueACurationForTheAuthor() => _state.CurationRequests[Okf].ByAuthor.ShouldBeTrue();

        void ItShouldWithdrawTheApprovalAndThePin() => _state.Topics[Okf].ShouldSatisfyAllConditions(
            topic => topic.ApprovedAt.ShouldBeNull(),
            topic => topic.Pinned.ShouldBeFalse());
    }

    [Test]
    public async Task TheAuthorRejectsACurationWithoutAReason()
    {
        await _handler.Handle(new RejectTopicCurationCommand("learning--okf.md", "   "), CancellationToken.None);

        ItShouldRecordTheRejectionWithoutAReason();

        void ItShouldRecordTheRejectionWithoutAReason() =>
            _state.Topics[Okf].Guidance.ShouldNotBeNull().ShouldHaveSingleItem().Text.ShouldBeNull();
    }

    [Test]
    public async Task TheAuthorRejectsMoreOftenThanATopicKeeps()
    {
        foreach (string reason in new[] { "First.", "Second.", "Third." })
        {
            await _handler.Handle(new RejectTopicCurationCommand("learning--okf.md", reason), CancellationToken.None);
        }

        ItShouldKeepOnlyTheLatestReasons();

        void ItShouldKeepOnlyTheLatestReasons() =>
            _state.Topics[Okf].Guidance.ShouldNotBeNull().Select(entry => entry.Text).ShouldBe(["Second.", "Third."]);
    }

    [Test]
    public async Task TheAuthorRejectsATopicNotCuratedYet()
    {
        _state.Topics[Okf] = ResearchTopic.New(Okf, Discovered);

        Result<ResearchTopic, Error> result = await _handler.Handle(new RejectTopicCurationCommand("learning--okf.md", null), CancellationToken.None);

        ItShouldBeRefused();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<TopicNotCuratedError>();
    }
}
