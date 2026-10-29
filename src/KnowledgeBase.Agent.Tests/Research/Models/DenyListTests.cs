using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Errors;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research.Models;

public sealed class DenyListTests
{
    private readonly DenyList _denyList = new(
    [
        new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied),
        new DenyListEntry("Contoso", SubjectKind.Organisation, DenyListSource.Manual, DenyListState.Allowed)
    ]);

    [Test]
    public void ANewPersonIsClassified()
    {
        DenyList updated = _denyList.WithAutomatic("  John   Smith ", SubjectKind.Person);

        ItShouldDenyThemAutomatically();

        void ItShouldDenyThemAutomatically() => updated.Find("John Smith")
            .ShouldBe(new DenyListEntry("John Smith", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied));
    }

    [Test]
    public void ANewPublicFigureIsClassified()
    {
        DenyList updated = _denyList.WithAutomatic("Martin Fowler", SubjectKind.PublicFigure, DenyListState.Allowed);

        ItShouldAllowThemAutomatically();

        void ItShouldAllowThemAutomatically() => updated.Find("Martin Fowler")
            .ShouldBe(new DenyListEntry("Martin Fowler", SubjectKind.PublicFigure, DenyListSource.Auto, DenyListState.Allowed));
    }

    [Test]
    public void APublicFigureIsLaterClassifiedAsAPerson()
    {
        DenyList updated = _denyList
            .WithAutomatic("Martin Fowler", SubjectKind.PublicFigure, DenyListState.Allowed)
            .WithAutomatic("Martin Fowler", SubjectKind.Person);

        ItShouldDenyThem();

        void ItShouldDenyThem() => updated.Find("Martin Fowler")
            .ShouldBe(new DenyListEntry("Martin Fowler", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied));
    }

    [Test]
    public void APossiblePublicFigureIsLaterClassifiedAsAPerson()
    {
        DenyList updated = _denyList
            .WithAutomatic("Sam", SubjectKind.Person, DenyListState.Pending)
            .WithAutomatic("Sam", SubjectKind.Person);

        ItShouldDenyThem();

        void ItShouldDenyThem() => updated.Find("Sam")!.State.ShouldBe(DenyListState.Denied);
    }

    [Test]
    [Arguments(DenyListState.Allowed)]
    [Arguments(DenyListState.Pending)]
    public void ADeniedPersonIsLaterClassifiedAsAPublicFigure(DenyListState state)
    {
        DenyList updated = _denyList.WithAutomatic("Jane Doe", SubjectKind.PublicFigure, state);

        ItShouldChangeNothing();

        void ItShouldChangeNothing() => updated.ShouldBeSameAs(_denyList);
    }

    [Test]
    [Arguments("Contoso")]
    [Arguments("CONTOSO")]
    public void AnOrganisationTheAuthorAllowedIsClassifiedAgain(string name)
    {
        DenyList updated = _denyList.WithAutomatic(name, SubjectKind.Organisation);

        ItShouldKeepTheAuthorsDecision();
        ItShouldChangeNothing();

        void ItShouldKeepTheAuthorsDecision() => updated.Find("Contoso")
            .ShouldBe(new DenyListEntry("Contoso", SubjectKind.Organisation, DenyListSource.Manual, DenyListState.Allowed));

        void ItShouldChangeNothing() => updated.ShouldBeSameAs(_denyList);
    }

    [Test]
    [Arguments("jane doe")]
    [Arguments("Jane-Doe")]
    [Arguments("jane_doe")]
    public void APersonAlreadyDeniedIsClassifiedAgain(string name)
    {
        DenyList updated = _denyList.WithAutomatic(name, SubjectKind.Person);

        ItShouldChangeNothing();

        void ItShouldChangeNothing() => updated.ShouldBeSameAs(_denyList);
    }

    [Test]
    [Arguments("   ")]
    [Arguments(" - ")]
    [Arguments("_")]
    public void ANameWithoutWordsIsClassified(string name)
    {
        DenyList updated = _denyList.WithAutomatic(name, SubjectKind.Person);

        ItShouldChangeNothing();

        void ItShouldChangeNothing() => updated.ShouldBeSameAs(_denyList);
    }

    [Test]
    public void TheAuthorAllowsAnAutomaticEntry()
    {
        Result<DenyList, Error> updated = _denyList.WithManual("jane doe", DenyListState.Allowed);

        ItShouldRecordTheDecisionAsTheAuthors();
        ItShouldLeaveTheOtherEntriesAlone();

        void ItShouldRecordTheDecisionAsTheAuthors() => updated.Value.Find("Jane Doe")
            .ShouldBe(new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Manual, DenyListState.Allowed));

        void ItShouldLeaveTheOtherEntriesAlone() => updated.Value.Find("Contoso").ShouldBe(_denyList.Find("Contoso"));
    }

    [Test]
    public void TheAuthorDeniesAnEntryTheyAllowed()
    {
        Result<DenyList, Error> updated = _denyList.WithManual("Contoso", DenyListState.Denied);

        ItShouldDenyItAsTheAuthorsDecision();

        void ItShouldDenyItAsTheAuthorsDecision() => updated.Value.Find("Contoso")
            .ShouldBe(new DenyListEntry("Contoso", SubjectKind.Organisation, DenyListSource.Manual, DenyListState.Denied));
    }

    [Test]
    public void TheAuthorTogglesANameThatIsNotListed()
    {
        Result<DenyList, Error> updated = _denyList.WithManual("Fabrikam", DenyListState.Allowed);

        ItShouldBeRefused();

        void ItShouldBeRefused() => updated.Error.ShouldBeOfType<DenyListEntryNotFoundError>().Name.ShouldBe("Fabrikam");
    }

    [Test]
    public void ClassificationRunsAfterTheAuthorDecided()
    {
        DenyList updated = _denyList
            .WithManual("Jane Doe", DenyListState.Allowed).Value
            .WithAutomatic("Jane Doe", SubjectKind.Person);

        ItShouldKeepTheAuthorsDecision();

        void ItShouldKeepTheAuthorsDecision() => updated.Find("Jane Doe")!.ShouldSatisfyAllConditions(
            entry => entry.Source.ShouldBe(DenyListSource.Manual),
            entry => entry.State.ShouldBe(DenyListState.Allowed));
    }

    [Test]
    public void AMatcherIsBuilt()
    {
        DenyListMatcher matcher = _denyList.Matcher();

        ItShouldFindDeniedNames();
        ItShouldIgnoreAllowedNames();

        void ItShouldFindDeniedNames() => matcher.FindDeniedName("ask jane doe").ShouldBe(Maybe.From("Jane Doe"));

        void ItShouldIgnoreAllowedNames() => matcher.FindDeniedName("contoso release notes").HasNoValue.ShouldBeTrue();
    }

    [Test]
    public void AMatcherIsBuiltWithAPendingName()
    {
        DenyListMatcher matcher = _denyList.WithAutomatic("Sam Smith", SubjectKind.Person, DenyListState.Pending).Matcher();

        ItShouldKeepThePendingNameOut();

        void ItShouldKeepThePendingNameOut() => matcher.FindDeniedName("ask sam smith").ShouldBe(Maybe.From("Sam Smith"));
    }

    [Test]
    [Arguments(DenyListState.Allowed)]
    [Arguments(DenyListState.Denied)]
    public void TheAuthorDecidesAPendingName(DenyListState decision)
    {
        Result<DenyList, Error> updated = _denyList
            .WithAutomatic("Sam Smith", SubjectKind.Person, DenyListState.Pending)
            .WithManual("Sam Smith", decision);

        ItShouldRecordTheDecisionAsTheAuthors();

        void ItShouldRecordTheDecisionAsTheAuthors() => updated.Value.Find("Sam Smith")
            .ShouldBe(new DenyListEntry("Sam Smith", SubjectKind.Person, DenyListSource.Manual, decision));
    }
}
