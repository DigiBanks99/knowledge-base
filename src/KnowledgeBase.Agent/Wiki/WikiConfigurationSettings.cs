namespace KnowledgeBase.Agent.Wiki;

public class WikiConfigurationSettings
{
    public const string SectionName = "Wiki";

    public required string RootDirectory { get; init; }
    public string NotesDirectory => Path.Combine(RootDirectory, "notes");
    public string NewNotesDirectory => Path.Combine(NotesDirectory, "new");
    public string ProcessingNotesDirectory => Path.Combine(NotesDirectory, "processing");
    public string ProcessedNotesDirectory => Path.Combine(NotesDirectory, "processed");
    public string FactsDirectory => Path.Combine(RootDirectory, "facts");
    public string ThreadsDirectory => Path.Combine(RootDirectory, "threads");
    public string LogsDirectory => Path.Combine(RootDirectory, "logs");
    public string WikiDirectory => Path.Combine(RootDirectory, "wiki");
}