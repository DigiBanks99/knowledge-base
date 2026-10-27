using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research.Models;

public sealed class TopicQueriesTests
{
    private const string Brief = "Releases and migration guides for the Atlas framework.";

    private readonly DenyListMatcher _denied = new DenyList(
    [
        new DenyListEntry("Jane Doe", SubjectKind.Person, DenyListSource.Auto, DenyListState.Denied),
        new DenyListEntry("Contoso", SubjectKind.Organisation, DenyListSource.Auto, DenyListState.Denied),
        new DenyListEntry("Microsoft", SubjectKind.Organisation, DenyListSource.Manual, DenyListState.Allowed)
    ]).Matcher();

    [Test]
    public void TheBriefAndQueriesAreFine()
    {
        Result<TopicQueries, Error> result = TopicQueries.Validate(
            $"  {Brief}  ",
            ["  atlas framework release  ", "atlas migration guide", "Microsoft atlas integration"],
            _denied);

        ItShouldAcceptThemTrimmed();

        void ItShouldAcceptThemTrimmed() => result.Value.ShouldSatisfyAllConditions(
            curated => curated.Brief.ShouldBe(Brief),
            curated => curated.Queries.ShouldBe(["atlas framework release", "atlas migration guide", "Microsoft atlas integration"]));
    }

    [Test]
    [Arguments(0)]
    [Arguments(4)]
    public void TheWrongNumberOfQueriesIsGiven(int count)
    {
        string[] queries = [.. Enumerable.Range(1, count).Select(index => $"atlas query {index}")];

        Result<TopicQueries, Error> result = TopicQueries.Validate(Brief, queries, _denied);

        ItShouldBeRefused();
        ItShouldSayHowManyAreAllowed();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("WrongNumberOfQueries");

        void ItShouldSayHowManyAreAllowed() => result.Error.Message.ShouldBe(
            $"You gave {count} queries; give between 1 and 3. Keep the most useful ones.");
    }

    [Test]
    public void AQueryIsTooLong()
    {
        string query = new('a', TopicQueries.MaxQueryLength + 1);

        Result<TopicQueries, Error> result = TopicQueries.Validate(Brief, ["atlas release", query], _denied);

        ItShouldNameTheQueryAndTheLimit();

        void ItShouldNameTheQueryAndTheLimit() => result.Error.ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("QueryTooLong"),
            error => error.Message.ShouldStartWith("Query 2 ("),
            error => error.Message.ShouldContain("at most 100 are allowed"));
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public void AQueryIsBlank(string query)
    {
        Result<TopicQueries, Error> result = TopicQueries.Validate(Brief, [query], _denied);

        ItShouldBeRefused();

        void ItShouldBeRefused() => result.Error.Code.ShouldBe("EmptyQuery");
    }

    [Test]
    public void AQueryRepeatsAnother()
    {
        Result<TopicQueries, Error> result = TopicQueries.Validate(Brief, ["atlas release", "Atlas Release"], _denied);

        ItShouldBeRefused();

        void ItShouldBeRefused() => result.Error.Code.ShouldBe("DuplicateQuery");
    }

    [Test]
    [Arguments(null)]
    [Arguments(" ")]
    public void TheBriefIsMissing(string? brief)
    {
        Result<TopicQueries, Error> result = TopicQueries.Validate(brief, ["atlas release"], _denied);

        ItShouldBeRefused();

        void ItShouldBeRefused() => result.Error.Code.ShouldBe("EmptyBrief");
    }

    [Test]
    public void TheBriefIsTooLong()
    {
        Result<TopicQueries, Error> result = TopicQueries.Validate(new string('b', TopicQueries.MaxBriefLength + 1), ["atlas release"], _denied);

        ItShouldBeRefused();

        void ItShouldBeRefused() => result.Error.Code.ShouldBe("BriefTooLong");
    }

    [Test]
    [Arguments("ask jane doe about atlas")]
    [Arguments("CONTOSO atlas rollout")]
    [Arguments("jane-doe atlas talk")]
    public void AQueryNamesSomeoneDenied(string query)
    {
        Result<TopicQueries, Error> result = TopicQueries.Validate(Brief, ["atlas release", query], _denied);

        ItShouldBeRefused();
        ItShouldSayWhichQueryAndName();

        void ItShouldBeRefused() => result.Error.ShouldBeOfType<ValidationError>().Code.ShouldBe("DeniedNameInQuery");

        void ItShouldSayWhichQueryAndName() => result.Error.Message.ShouldStartWith($"Query 2 ('{query}') contains the name '");
    }

    [Test]
    public void AQueryContainsADeniedNameInsideAnotherWord()
    {
        Result<TopicQueries, Error> result = TopicQueries.Validate(Brief, ["contosoville atlas meetup"], _denied);

        ItShouldBeAccepted();

        void ItShouldBeAccepted() => result.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void TheBriefNamesSomeoneDenied()
    {
        Result<TopicQueries, Error> result = TopicQueries.Validate("What Jane Doe should know about Atlas.", ["atlas release"], _denied);

        ItShouldBeRefused();
        ItShouldNameThem();

        void ItShouldBeRefused() => result.Error.Code.ShouldBe("DeniedNameInBrief");

        void ItShouldNameThem() => result.Error.Message.ShouldContain("'Jane Doe'");
    }
}
