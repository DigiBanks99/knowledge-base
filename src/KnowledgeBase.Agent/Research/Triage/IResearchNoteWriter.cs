using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Research.Triage;

/// <summary>Puts research notes in the notes inbox, where the pipeline picks them up like the author's own.</summary>
internal interface IResearchNoteWriter
{
    /// <summary>Whether the note is anywhere in the notes folders, so a note is never written twice.</summary>
    bool Exists(ResearchNote note);

    Task WriteAsync(ResearchNote note, CancellationToken cancellationToken);
}

internal sealed class ResearchNoteWriter(IOptions<WikiConfigurationSettings> settings) : IResearchNoteWriter
{
    private readonly WikiConfigurationSettings _settings = settings.Value;

    public bool Exists(ResearchNote note) =>
        new[]
        {
            _settings.NewNotesDirectory,
            _settings.ProcessingNotesDirectory,
            _settings.ProcessedNotesDirectory,
            _settings.FailedNotesDirectory
        }.Any(directory => File.Exists(Path.Combine(directory, note.FileName)));

    /// <summary>Written outside the notes folders and moved in, so the watcher never reads half a note.</summary>
    public async Task WriteAsync(ResearchNote note, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_settings.ResearchDirectory);
        string temporary = Path.Combine(_settings.ResearchDirectory, $"{note.FileName}.tmp");
        await File.WriteAllTextAsync(temporary, note.Content, cancellationToken);
        File.Move(temporary, Path.Combine(_settings.NewNotesDirectory, note.FileName), overwrite: true);
    }
}
