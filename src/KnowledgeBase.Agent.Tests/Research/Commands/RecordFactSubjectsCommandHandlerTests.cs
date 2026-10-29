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
    public async Task TheAuthorsNoteCitesPublicFigures()
    {
        Fact[] facts =
        [
            new(
                new FactSubject("Refactoring", null, SubjectKind.Other),
                [
                    new Observation(
                        "Read an article on refactoring.",
                        "Recommended by Sam.",
                        1,
                        [
                            new FactSubject("Martin Fowler", "Author of the article.", SubjectKind.PublicFigure),
                            new FactSubject("Kent Beck", "Quoted in the article.", SubjectKind.PublicFigure, PossiblyPublicFigure: true),
                            new FactSubject("Sam", "Recommended the article.", SubjectKind.Person, PossiblyPublicFigure: true),
                            new FactSubject("Fabrikam", "Hosts the article.", SubjectKind.Organisation, PossiblyPublicFigure: true)
                        ])
                ])
        ];

        Result<int, Error> result = await _handler.Handle(new RecordFactSubjectsCommand(facts, FactOrigin.Author), CancellationToken.None);

        ItShouldCountThemAll();
        ItShouldAllowThePublicFigure();
        ItShouldLeaveThePossiblePublicFiguresToTheAuthor();
        ItShouldDenyTheOrganisation();

        void ItShouldCountThemAll() => result.Value.ShouldBe(4);

        void ItShouldAllowThePublicFigure() => _denyList().Find("Martin Fowler")
            .ShouldBe(new DenyListEntry("Martin Fowler", SubjectKind.PublicFigure, DenyListSource.Auto, DenyListState.Allowed));

        void ItShouldLeaveThePossiblePublicFiguresToTheAuthor()
        {
            _denyList().Find("Kent Beck")!.State.ShouldBe(DenyListState.Pending);
            _denyList().Find("Sam")
                .ShouldBe(new DenyListEntry("Sam", SubjectKind.Person, DenyListSource.Auto, DenyListState.Pending));
        }

        void ItShouldDenyTheOrganisation() => _denyList().Find("Fabrikam")!.State.ShouldBe(DenyListState.Denied);
    }

    [Test]
    public async Task OneNoteNamesSomeoneAsAPublicFigureAndAPerson()
    {
        Fact[] facts =
        [
            new(new FactSubject("Jane Smith", "A friend.", SubjectKind.Person), []),
            new(new FactSubject("Refactoring", null, SubjectKind.Other),
                [new Observation("Jane wrote about refactoring.", "Her blog post.", 1, [new FactSubject("Jane Smith", "Author of the post.", SubjectKind.PublicFigure)])])
        ];

        await _handler.Handle(new RecordFactSubjectsCommand(facts, FactOrigin.Author), CancellationToken.None);

        ItShouldDenyThem();

        void ItShouldDenyThem() => _denyList().Find("Jane Smith")!.State.ShouldBe(DenyListState.Denied);
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
