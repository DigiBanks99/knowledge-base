using KnowledgeBase.Agent.Notes.Queries;

namespace KnowledgeBase.Agent.Tests.Notes;

public sealed class NoteCatalogTests
{
    [Test]
    [Arguments("note-2026-10-24-101500.md")]
    [Arguments("NOTE.MD")]
    public void AWellFormedNoteNameIsChecked(string name)
    {
        bool valid = NoteCatalog.IsValidName(name);

        ItShouldBeAccepted();

        void ItShouldBeAccepted() => valid.ShouldBeTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("note-2026-10-24-101500.txt")]
    [Arguments("processed/note.md")]
    [Arguments("../note.md")]
    public void AMalformedNoteNameIsChecked(string name)
    {
        bool valid = NoteCatalog.IsValidName(name);

        ItShouldBeRejected();

        void ItShouldBeRejected() => valid.ShouldBeFalse();
    }
}