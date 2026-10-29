using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class SetDenyListEntryStateCommandHandlerTests
{
    private readonly DenyList _initial = new([new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied)]);
    private readonly Func<DenyList> _denyList;
    private readonly SetDenyListEntryStateCommandHandler _handler;

    public SetDenyListEntryStateCommandHandlerTests()
    {
        IResearchStore store = FakeResearchStore.Holding(_initial, out _denyList);
        _handler = new SetDenyListEntryStateCommandHandler(NullLogger<SetDenyListEntryStateCommandHandler>.Instance, store);
    }

    [Test]
    public async Task TheAuthorAllowsAName()
    {
        Result<DenyListEntry, Error> result = await _handler.Handle(
            new SetDenyListEntryStateCommand("jane doe", DenyListState.Allowed),
            CancellationToken.None);

        ItShouldReturnTheEntry();
        ItShouldStoreTheAuthorsDecision();

        void ItShouldReturnTheEntry() =>
            result.Value.ShouldBe(new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Manual, DenyListState.Allowed));

        void ItShouldStoreTheAuthorsDecision() => _denyList().Find("Jane Doe").ShouldBe(result.Value);
    }

    [Test]
    public async Task TheAuthorTogglesAnUnknownName()
    {
        Result<DenyListEntry, Error> result = await _handler.Handle(
            new SetDenyListEntryStateCommand("John Smith", DenyListState.Allowed),
            CancellationToken.None);

        ItShouldBeNotFound();
        ItShouldChangeNothing();

        void ItShouldBeNotFound() => result.Error.ShouldBeOfType<DenyListEntryNotFoundError>();

        void ItShouldChangeNothing() => _denyList().ShouldBeSameAs(_initial);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task TheAuthorTogglesWithoutAName(string name)
    {
        Result<DenyListEntry, Error> result = await _handler.Handle(
            new SetDenyListEntryStateCommand(name, DenyListState.Allowed),
            CancellationToken.None);

        ItShouldBeRefused();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("DenyListNameRequired");
    }

    [Test]
    public async Task TheAuthorSetsANamePending()
    {
        Result<DenyListEntry, Error> result = await _handler.Handle(
            new SetDenyListEntryStateCommand("Jane Doe", DenyListState.Pending),
            CancellationToken.None);

        ItShouldBeRefused();
        ItShouldChangeNothing();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("DenyListStateInvalid");

        void ItShouldChangeNothing() => _denyList().ShouldBeSameAs(_initial);
    }
}
