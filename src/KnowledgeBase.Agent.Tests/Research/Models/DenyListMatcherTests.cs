using KnowledgeBase.Agent.Research.Models;

namespace KnowledgeBase.Agent.Tests.Research.Models;

public sealed class DenyListMatcherTests
{
    private readonly DenyListMatcher _matcher = new(["Ann", "Jane Doe", "C# Guild"]);

    [Test]
    [Arguments("what did ann say", "Ann")]
    [Arguments("ANN's new role", "Ann")]
    [Arguments("(Ann)", "Ann")]
    [Arguments("jane doe conference talk", "Jane Doe")]
    [Arguments("Jane-Doe on github", "Jane Doe")]
    [Arguments("jane_doe", "Jane Doe")]
    [Arguments("Jane   Doe", "Jane Doe")]
    [Arguments("c# guild meetup", "C# Guild")]
    public void ATextNamesADeniedName(string text, string name)
    {
        Maybe<string> found = _matcher.FindDeniedName(text);

        ItShouldFindTheName();

        void ItShouldFindTheName() => found.ShouldBe(Maybe.From(name));
    }

    [Test]
    [Arguments("annual report")]
    [Arguments("joanne")]
    [Arguments("ann2")]
    [Arguments("jane doesnt")]
    [Arguments("jane, doe")]
    [Arguments("")]
    public void ATextOnlyContainsPartOfADeniedName(string text)
    {
        Maybe<string> found = _matcher.FindDeniedName(text);

        ItShouldFindNothing();

        void ItShouldFindNothing() => found.HasNoValue.ShouldBeTrue();
    }

    [Test]
    public void TheDenyListIsEmpty()
    {
        Maybe<string> found = new DenyListMatcher([]).FindDeniedName("anything at all");

        ItShouldFindNothing();

        void ItShouldFindNothing() => found.HasNoValue.ShouldBeTrue();
    }
}