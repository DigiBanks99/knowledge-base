using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class ApproveTopicCurationCommandHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Atlas = "projects/atlas";

    private readonly ResearchTopics _state = new();
    private readonly ApproveTopicCurationCommandHandler _handler;

    public ApproveTopicCurationCommandHandlerTests()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Discovered) with { Brief = "News about Atlas.", Queries = ["atlas release"] };
        TimeProvider timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(Now);
        _handler = new ApproveTopicCurationCommandHandler(
            NullLogger<ApproveTopicCurationCommandHandler>.Instance,
            FakeResearchStore.Holding(_state),
            new WikiPaths(Options.Create(new WikiConfigurationSettings { RootDirectory = "/data/kb" })),
            timeProvider);
    }

    [Test]
    public async Task TheAuthorApprovesACuration()
    {
        await _handler.Handle(new ApproveTopicCurationCommand("projects--atlas"), CancellationToken.None);

        ItShouldRecordTheApproval();
        ItShouldNotQueueACuration();

        void ItShouldRecordTheApproval() => _state.Topics[Atlas].ApprovedAt.ShouldBe(Now);

        void ItShouldNotQueueACuration() => _state.CurationRequests.ShouldBeEmpty();
    }

    [Test]
    public async Task TheAuthorApprovesATopicWithAnOpenQuestion()
    {
        _state.Topics[Atlas] = _state.Topics[Atlas] with { ReviewQuestion = "Which Atlas?" };

        Result<ResearchTopic, Error> result = await _handler.Handle(new ApproveTopicCurationCommand("projects--atlas"), CancellationToken.None);

        ItShouldBeRefused();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<TopicNeedsReviewError>();
    }

    [Test]
    public async Task TheAuthorApprovesATopicNotCuratedYet()
    {
        _state.Topics[Atlas] = ResearchTopic.New(Atlas, Discovered);

        Result<ResearchTopic, Error> result = await _handler.Handle(new ApproveTopicCurationCommand("projects--atlas"), CancellationToken.None);

        ItShouldBeRefused();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<TopicNotCuratedError>();
    }
}
