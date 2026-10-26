using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class SetTopicMutedCommandHandlerTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);

    private readonly ResearchTopics _state = new();
    private readonly SetTopicMutedCommandHandler _handler;

    public SetTopicMutedCommandHandlerTests()
    {
        _state.Topics["projects/atlas"] = ResearchTopic.New("projects/atlas", Discovered) with { Brief = "A project." };
        WikiPaths wikiPaths = new(Options.Create(new WikiConfigurationSettings { RootDirectory = "/data/kb" }));
        _handler = new SetTopicMutedCommandHandler(
            NullLogger<SetTopicMutedCommandHandler>.Instance,
            FakeResearchStore.Holding(_state),
            wikiPaths);
    }

    [Test]
    public async Task TheAuthorMutesATopic()
    {
        Result<ResearchTopic, Error> result = await _handler.Handle(new SetTopicMutedCommand("projects--atlas", true), CancellationToken.None);

        ItShouldMuteIt();
        ItShouldKeepTheRestOfItsState();

        void ItShouldMuteIt() => result.Value.Muted.ShouldBeTrue();

        void ItShouldKeepTheRestOfItsState() => _state.Topics["projects/atlas"].ShouldSatisfyAllConditions(
            topic => topic.Muted.ShouldBeTrue(),
            topic => topic.Brief.ShouldBe("A project."),
            topic => topic.CreatedAt.ShouldBe(Discovered));
    }

    [Test]
    public async Task TheAuthorUnmutesATopic()
    {
        _state.Topics["projects/atlas"] = _state.Topics["projects/atlas"] with { Muted = true };

        await _handler.Handle(new SetTopicMutedCommand("projects--atlas", false), CancellationToken.None);

        ItShouldResumeResearch();

        void ItShouldResumeResearch() => _state.Topics["projects/atlas"].Muted.ShouldBeFalse();
    }

    [Test]
    public async Task TheAuthorMutesAnUnknownTopic()
    {
        Result<ResearchTopic, Error> result = await _handler.Handle(new SetTopicMutedCommand("projects--unknown", true), CancellationToken.None);

        ItShouldBeNotFound();
        ItShouldNotCreateIt();

        void ItShouldBeNotFound() => result.Error.ShouldBeOfType<TopicNotFoundError>().Topic.ShouldBe("projects/unknown");

        void ItShouldNotCreateIt() => _state.Topics.Keys.ShouldBe(["projects/atlas"]);
    }

    [Test]
    [Arguments("..--..--notes")]
    [Arguments("people--jane-doe")]
    [Arguments("projects--..")]
    [Arguments("projects--atlas/../../../etc")]
    [Arguments("projects")]
    public async Task TheUIAsksForSomethingThatIsNotATopic(string slug)
    {
        Result<ResearchTopic, Error> result = await _handler.Handle(new SetTopicMutedCommand(slug, true), CancellationToken.None);

        ItShouldBeRefused();
        ItShouldChangeNothing();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("InvalidTopic");

        void ItShouldChangeNothing() => _state.Topics["projects/atlas"].Muted.ShouldBeFalse();
    }
}
