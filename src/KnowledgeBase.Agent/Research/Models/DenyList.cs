using System.Text.RegularExpressions;

using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Errors;

namespace KnowledgeBase.Agent.Research.Models;

/// <summary>
/// The names kept out of search queries, and the rules for changing them: classification only ever adds names, and
/// a decision the author made in the UI (<see cref="DenyListSource.Manual"/>) is never changed by classification.
/// </summary>
/// <param name="SeededFromPeople">Whether the one-off backfill from the wiki's <c>people/</c> folders has run.</param>
public sealed partial record DenyList(IReadOnlyList<DenyListEntry> Entries, bool SeededFromPeople = false)
{
    public static DenyList Empty { get; } = new([]);

    /// <summary>
    /// Adds a name classified from the author's own notes as denied, unless an entry for it already exists: an
    /// existing entry is either already denied or was allowed by the author, and both must stay as they are.
    /// </summary>
    public DenyList WithAutomatic(string name, SubjectKind kind)
    {
        string trimmed = WhitespaceRun().Replace(name.Trim(), " ");
        // A name of only separators has no words to match, so it would be listed but deny nothing.
        return DenyListMatcher.Words(trimmed).Length == 0 || Find(trimmed) is not null
            ? this
            : this with { Entries = [.. Entries, new DenyListEntry(trimmed, kind, DenyListSource.Auto, DenyListState.Denied)] };
    }

    /// <summary>Records the author's decision for an existing entry, which classification will never change again.</summary>
    public Result<DenyList, Error> WithManual(string name, DenyListState state)
    {
        DenyListEntry? entry = Find(name);
        return entry is null
            ? new DenyListEntryNotFoundError(name)
            : this with
            {
                Entries = [.. Entries.Select(existing => existing == entry
                    ? existing with { Source = DenyListSource.Manual, State = state }
                    : existing)]
            };
    }

    public DenyListEntry? Find(string name)
    {
        string key = Key(name);
        return Entries.FirstOrDefault(entry => Key(entry.Name) == key);
    }

    public DenyListMatcher Matcher() => new([.. Entries.Where(entry => entry.State == DenyListState.Denied).Select(entry => entry.Name)]);

    /// <summary>
    /// Names are the same entry whatever their case or how their words are separated, so <c>jane-doe</c> from a folder
    /// and <c>Jane Doe</c> from a note never become two entries.
    /// </summary>
    internal static string Key(string name) =>
        string.Join(' ', DenyListMatcher.Words(name)).ToUpperInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}