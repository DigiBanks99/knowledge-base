using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Notes.Queries;

public enum NoteState
{
    New,
    Processing,
    Processed,
    Failed
}

public sealed record NoteSummary(string Name, NoteState State, DateTimeOffset CapturedAt, DateTimeOffset UpdatedAt, string Preview);

public sealed record NoteDetail(NoteSummary Summary, string Content);

/// <summary>Finds notes across the pipeline folders for the UI. The folder a note is in is its state.</summary>
internal sealed class NoteCatalog(IOptions<WikiConfigurationSettings> options)
{
    private const int PreviewLength = 200;

    private readonly WikiConfigurationSettings _settings = options.Value;

    public async Task<IReadOnlyList<NoteSummary>> ListAsync(CancellationToken cancellationToken)
    {
        List<NoteSummary> notes = [];
        foreach ((NoteState state, string directory) in StateDirectories())
        {
            foreach (string filePath in Directory.EnumerateFiles(directory, "*.md"))
            {
                notes.Add(await SummariseAsync(state, filePath, cancellationToken));
            }
        }

        return notes.OrderByDescending(note => note.CapturedAt).ToList();
    }

    public async Task<NoteDetail?> GetAsync(string name, CancellationToken cancellationToken)
    {
        if (Find(name) is not (NoteState state, string filePath))
        {
            return null;
        }

        string content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return new NoteDetail(await SummariseAsync(state, filePath, cancellationToken), content);
    }

    public (NoteState State, string FilePath)? Find(string name)
    {
        if (!IsValidName(name))
        {
            return null;
        }

        foreach ((NoteState state, string directory) in StateDirectories())
        {
            string filePath = Path.Combine(directory, name);
            if (File.Exists(filePath))
            {
                return (state, filePath);
            }
        }

        return null;
    }

    public static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name == Path.GetFileName(name)
        && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private IEnumerable<(NoteState State, string Directory)> StateDirectories() =>
    [
        (NoteState.New, _settings.NewNotesDirectory),
        (NoteState.Processing, _settings.ProcessingNotesDirectory),
        (NoteState.Processed, _settings.ProcessedNotesDirectory),
        (NoteState.Failed, _settings.FailedNotesDirectory)
    ];

    private static async Task<NoteSummary> SummariseAsync(NoteState state, string filePath, CancellationToken cancellationToken)
    {
        FileInfo file = new(filePath);
        string content = await File.ReadAllTextAsync(filePath, cancellationToken);
        string preview = content.Length > PreviewLength ? $"{content[..PreviewLength].TrimEnd()}…" : content;
        return new NoteSummary(file.Name, state, CapturedAt(file), file.LastWriteTimeUtc, preview);
    }

    /// <summary>
    /// Captured notes are named <c>note-yyyy-MM-dd-HHmmss.md</c>. File creation times are not reliable on every
    /// platform (or after a move), so the name is preferred.
    /// </summary>
    private static DateTimeOffset CapturedAt(FileInfo file)
    {
        string stamp = Path.GetFileNameWithoutExtension(file.Name).Replace("note-", string.Empty, StringComparison.OrdinalIgnoreCase);
        return DateTimeOffset.TryParseExact(
            stamp,
            "yyyy-MM-dd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out DateTimeOffset capturedAt)
            ? capturedAt
            : file.LastWriteTimeUtc;
    }
}
