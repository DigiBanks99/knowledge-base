using System.Text.RegularExpressions;

using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Research.Errors;

namespace KnowledgeBase.Agent.Research.Models;

/// <summary>
/// The names kept out of search queries, and the rules for changing them: classification only ever adds names or
/// makes an automatic entry more private, and a decision the author made in the UI (<see cref="DenyListSource.Manual"/>)
/// is never changed by classification.
/// </summary>
/// <param name="SeededFromPeople">Whether the one-off backfill from the wiki's <c>people/</c> folders has run.</param>
public sealed partial record DenyList(IReadOnlyList<DenyListEntry> Entries, bool SeededFromPeople = false)
{
    public static DenyList Empty { get; } = new([]);

    /// <summary>
    /// Adds a name classified from the author's own notes. An existing automatic entry only ever becomes more private,
    /// so a public figure a later note shows the author knows is denied, while one denied before is never allowed by a
    /// later note. The author's own decisions stay as they are.
    /// </summary>
    public DenyList WithAutomatic(string name, SubjectKind kind, DenyListState state = DenyListState.Denied)
    {
        string trimmed = WhitespaceRun().Replace(name.Trim(), " ");
        // A name of only separators has no words to match, so it would be listed but deny nothing.
        if (DenyListMatcher.Words(trimmed).Length == 0)
        {
            return this;
        }

        DenyListEntry? existing = Find(trimmed);
        if (existing is null)
        {
            return this with { Entries = [.. Entries, new DenyListEntry(trimmed, kind, DenyListSource.Auto, state)] };
        }

        return existing.Source == DenyListSource.Manual || Privacy(state) <= Privacy(existing.State)
            ? this
            : this with
            {
                Entries = [.. Entries.Select(entry => entry == existing ? entry with { Kind = kind, State = state } : entry)]
            };
    }

    /// <summary>
    /// Records the author's decision about a name, listed or not, which classification will never change again. An
    /// existing entry keeps its spelling.
    /// </summary>
    public DenyList WithManualEntry(string name, SubjectKind kind, DenyListState state)
    {
        DenyListEntry? existing = Find(name);
        return existing is null
            ? this with
            {
                Entries = [.. Entries, new DenyListEntry(WhitespaceRun().Replace(name.Trim(), " "), kind, DenyListSource.Manual, state)]
            }
            : this with
            {
                Entries = [.. Entries.Select(entry => entry == existing
                    ? entry with { Kind = kind, Source = DenyListSource.Manual, State = state }
                    : entry)]
            };
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

    /// <summary>Matches every name not allowed: a pending name stays out of queries until the author allows it.</summary>
    public DenyListMatcher Matcher() => new([.. Entries.Where(entry => entry.State != DenyListState.Allowed).Select(entry => entry.Name)]);

    /// <summary>
    /// Names are the same entry whatever their case or how their words are separated, so <c>jane-doe</c> from a folder
    /// and <c>Jane Doe</c> from a note never become two entries.
    /// </summary>
    internal static string Key(string name) =>
        string.Join(' ', DenyListMatcher.Words(name)).ToUpperInvariant();

    private static int Privacy(DenyListState state) => state switch
    {
        DenyListState.Allowed => 0,
        DenyListState.Pending => 1,
        _ => 2
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
