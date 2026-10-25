using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research;
using KnowledgeBase.Agent.Research.Commands;
using KnowledgeBase.Agent.Research.Models;

using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

public sealed class RecordFactSubjectsCommandHandlerTests
{
    private static readonly Fact[] Facts =
    [
        new(
            new FactSubject("Jane Doe", "A colleague.", SubjectKind.Person),
            [
                new Observation(
                    "Jane joined Contoso.",
                    "She moved teams in October.",
                    2,
                    [
                        new FactSubject("Contoso", "Her employer.", SubjectKind.Organisation),
                        new FactSubject("Kubernetes", "A technology.", SubjectKind.Other)
                    ])
            ]),
        new(new FactSubject("Project Atlas", "The author's project.", SubjectKind.Other), [])
    ];

    private readonly IResearchStore _store;
    private readonly Func<DenyList> _denyList;
    private readonly RecordFactSubjectsCommandHandler _handler;

    public RecordFactSubjectsCommandHandlerTests()
    {
        _store = FakeResearchStore.Holding(
            new DenyList([new DenyListEntry("Contoso", SubjectKind.Organisation, DenyListSource.Manual, DenyListState.Allowed)]),
            out _denyList);
        _handler = new RecordFactSubjectsCommandHandler(NullLogger<RecordFactSubjectsCommandHandler>.Instance, _store);
    }

    [Test]
    public async Task FactsFromTheAuthorsNoteAreRecorded()
    {
        Result<int, Error> result = await _handler.Handle(new RecordFactSubjectsCommand(Facts, FactOrigin.Author), CancellationToken.None);

        ItShouldCountThePeopleAndOrganisations();
        ItShouldDenyThePeople();
        ItShouldKeepTheAuthorsDecisionAboutAnOrganisation();
        ItShouldLeaveOtherSubjectsOut();

        void ItShouldCountThePeopleAndOrganisations() => result.Value.ShouldBe(2);

        void ItShouldDenyThePeople() => _denyList().Find("Jane Doe")
            .ShouldBe(new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied));

        void ItShouldKeepTheAuthorsDecisionAboutAnOrganisation() => _denyList().Find("Contoso")!.State.ShouldBe(DenyListState.Allowed);

        void ItShouldLeaveOtherSubjectsOut() => _denyList().Entries.Select(entry => entry.Name)
            .ShouldBe(["Contoso", "Jane Doe"], ignoreOrder: true);
    }

    [Test]
    public async Task ARelatedOrganisationIsNew()
    {
        Fact[] facts =
        [
            new(
                new FactSubject("Project Atlas", null, SubjectKind.Other),
                [new Observation("Built with Fabrikam.", "A supplier.", 1, [new FactSubject("Fabrikam", null, SubjectKind.Organisation)])])
        ];

        await _handler.Handle(new RecordFactSubjectsCommand(facts, FactOrigin.Author), CancellationToken.None);

        ItShouldDenyTheOrganisation();

        void ItShouldDenyTheOrganisation() => _denyList().Find("Fabrikam")
            .ShouldBe(new DenyListEntry("Fabrikam", SubjectKind.Organisation, DenyListSource.Auto, DenyListState.Denied));
    }

    [Test]
    public async Task FactsFromAResearchNoteAreRecorded()
    {
        Result<int, Error> result = await _handler.Handle(new RecordFactSubjectsCommand(Facts, FactOrigin.Research), CancellationToken.None);

        ItShouldCountNothing();
        await ItShouldNotTouchTheDenyList();

        void ItShouldCountNothing() => result.Value.ShouldBe(0);

        async Task ItShouldNotTouchTheDenyList()
        {
            await _store.DidNotReceive().ReadDenyListAsync(Arg.Any<CancellationToken>());
            await _store.DidNotReceive()
                .UpdateDenyListAsync(Arg.Any<Func<DenyList, Result<DenyList, Error>>>(), Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task TheAuthorsNoteNamesNoPeopleOrOrganisations()
    {
        Fact[] facts = [new(new FactSubject("Kubernetes", null, SubjectKind.Other), [])];

        await _handler.Handle(new RecordFactSubjectsCommand(facts, FactOrigin.Author), CancellationToken.None);

        await ItShouldNotChangeTheDenyList();

        async Task ItShouldNotChangeTheDenyList() => await _store.DidNotReceive()
            .UpdateDenyListAsync(Arg.Any<Func<DenyList, Result<DenyList, Error>>>(), Arg.Any<CancellationToken>());
    }
}
