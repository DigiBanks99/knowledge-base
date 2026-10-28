using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Triage;

/// <summary>Where in the notes folders a research note is, which says how far the pipeline has got with it.</summary>
public enum NoteLocation
{
    Missing,
    New,
    Processing,
    Processed,
    Failed
}

/// <summary>Puts research notes in the notes inbox, where the pipeline picks them up like the author's own.</summary>
internal interface IResearchNoteWriter
{
    /// <summary>Whether the note is anywhere in the notes folders, so a note is never written twice.</summary>
    bool Exists(ResearchNote note);

    /// <summary>Where the note of that file name is now.</summary>
    NoteLocation Locate(string fileName);

    Task WriteAsync(ResearchNote note, CancellationToken cancellationToken);
}

internal sealed class ResearchNoteWriter(IOptions<WikiConfigurationSettings> settings) : IResearchNoteWriter
{
    private readonly WikiConfigurationSettings _settings = settings.Value;

    public bool Exists(ResearchNote note) => Locate(note.FileName) != NoteLocation.Missing;

    /// <summary>
    /// The folders are checked in turn, so a note moving back (an interrupted note is requeued) can slip between two
    /// checks; it is only missing once a second look agrees.
    /// </summary>
    public NoteLocation Locate(string fileName)
    {
        NoteLocation location = LocateOnce(fileName);
        return location == NoteLocation.Missing ? LocateOnce(fileName) : location;
    }

    private NoteLocation LocateOnce(string fileName)
    {
        (NoteLocation Location, string Directory)[] folders =
        [
            (NoteLocation.New, _settings.NewNotesDirectory),
            (NoteLocation.Processing, _settings.ProcessingNotesDirectory),
            (NoteLocation.Processed, _settings.ProcessedNotesDirectory),
            (NoteLocation.Failed, _settings.FailedNotesDirectory)
        ];
        return folders.FirstOrDefault(folder => File.Exists(Path.Combine(folder.Directory, fileName))).Location;
    }

    /// <summary>Written outside the notes folders and moved in, so the watcher never reads half a note.</summary>
    public async Task WriteAsync(ResearchNote note, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_settings.ResearchDirectory);
        string temporary = Path.Combine(_settings.ResearchDirectory, $"{note.FileName}.tmp");
        await File.WriteAllTextAsync(temporary, note.Content, cancellationToken);
        File.Move(temporary, Path.Combine(_settings.NewNotesDirectory, note.FileName), overwrite: true);
    }
}
