using KnowledgeBase.Agent.Research.Commands;

namespace KnowledgeBase.Agent.Tests.Research.Commands;

/// <summary>Seeding reads the wiki's folders, so it is covered in the integration tests; the naming is pure.</summary>
public sealed class SeedDenyListFromPeopleCommandHandlerTests
{
    [Test]
    [Arguments("jane-doe", "Jane Doe")]
    [Arguments("mary-jane-van-der-berg", "Mary Jane Van Der Berg")]
    [Arguments("o'neill", "O'neill")]
    [Arguments("ann", "Ann")]
    [Arguments("JOHN_SMITH", "JOHN SMITH")]
    public void APersonsFolderIsNamed(string folder, string name)
    {
        string personName = SeedDenyListFromPeopleCommandHandler.PersonName(folder);

        ItShouldSpellOutTheName();

        void ItShouldSpellOutTheName() => personName.ShouldBe(name);
    }
}
