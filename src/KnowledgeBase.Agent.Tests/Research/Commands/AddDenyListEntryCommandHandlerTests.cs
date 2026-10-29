using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class AddDenyListEntryCommandHandlerTests
{
    private readonly DenyList _initial = new([new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied)]);
    private readonly IResearchStore _store;
    private readonly Func<DenyList> _denyList;
    private readonly AddDenyListEntryCommandHandler _handler;

    public AddDenyListEntryCommandHandlerTests()
    {
        _store = FakeResearchStore.Holding(_initial, out _denyList);
        _handler = new AddDenyListEntryCommandHandler(NullLogger<AddDenyListEntryCommandHandler>.Instance, _store);
    }

    [Test]
    public async Task TheAuthorAllowsAPublicFigureNoNoteNamedYet()
    {
        Result<DenyListEntry, Error> result = await _handler.Handle(
            new AddDenyListEntryCommand("  Martin   Fowler ", SubjectKind.PublicFigure, DenyListState.Allowed),
            CancellationToken.None);

        ItShouldReturnTheEntry();
        ItShouldStoreTheAuthorsDecision();

        void ItShouldReturnTheEntry() => result.Value
            .ShouldBe(new DenyListEntry("Martin Fowler", SubjectKind.PublicFigure, DenyListSource.Manual, DenyListState.Allowed));

        void ItShouldStoreTheAuthorsDecision() => _denyList().Find("martin fowler").ShouldBe(result.Value);
    }

    [Test]
    public async Task TheAuthorAddsANameAlreadyListed()
    {
        Result<DenyListEntry, Error> result = await _handler.Handle(
            new AddDenyListEntryCommand("jane-doe", SubjectKind.PublicFigure, DenyListState.Allowed),
            CancellationToken.None);

        ItShouldDecideTheExistingEntry();

        void ItShouldDecideTheExistingEntry() => _denyList().Entries.ShouldHaveSingleItem()
            .ShouldBe(new DenyListEntry("Jane Doe", SubjectKind.PublicFigure, DenyListSource.Manual, DenyListState.Allowed));
    }

    [Test]
    [Arguments("", SubjectKind.Person, DenyListState.Denied, "DenyListNameRequired")]
    [Arguments(" - ", SubjectKind.Person, DenyListState.Denied, "DenyListNameRequired")]
    [Arguments("Kubernetes", SubjectKind.Other, DenyListState.Denied, "DenyListKindInvalid")]
    [Arguments("Sam", SubjectKind.Person, DenyListState.Pending, "DenyListStateInvalid")]
    public async Task TheAuthorAddsSomethingThatCannotBeListed(string name, SubjectKind kind, DenyListState state, string code)
    {
        Result<DenyListEntry, Error> result = await _handler.Handle(new AddDenyListEntryCommand(name, kind, state), CancellationToken.None);

        ItShouldBeRefused();
        await ItShouldChangeNothing();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe(code);

        async Task ItShouldChangeNothing() => await _store.DidNotReceive()
            .UpdateDenyListAsync(Arg.Any<Func<DenyList, Result<DenyList, Error>>>(), Arg.Any<CancellationToken>());
    }
}
