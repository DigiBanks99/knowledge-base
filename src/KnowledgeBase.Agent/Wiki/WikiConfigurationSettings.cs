namespace KnowledgeBase.Agent.Wiki;

public class WikiConfigurationSettings
{
    public const string SectionName = "Wiki";

    public required string RootDirectory { get; init; }
    public string NotesDirectory => Path.Combine(RootDirectory, "notes");
    public string NewNotesDirectory => Path.Combine(NotesDirectory, "new");
    public string ProcessingNotesDirectory => Path.Combine(NotesDirectory, "processing");
    public string ProcessedNotesDirectory => Path.Combine(NotesDirectory, "processed");
    public string FailedNotesDirectory => Path.Combine(NotesDirectory, "failed");
    public string FactsDirectory => Path.Combine(RootDirectory, "facts");
    public string ProcessedFactsDirectory => Path.Combine(FactsDirectory, "processed");
    public string FailedFactsDirectory => Path.Combine(FactsDirectory, "failed");
    public string RunsDirectory => Path.Combine(RootDirectory, "runs");
    public string ConversationsDirectory => Path.Combine(RootDirectory, "conversations");
    public string ThreadsDirectory => Path.Combine(RootDirectory, "threads");
    public string LogsDirectory => Path.Combine(RootDirectory, "logs");
    public string KeysDirectory => Path.Combine(RootDirectory, "keys");
    public string ReviewDirectory => Path.Combine(RootDirectory, "review");
    public string ReviewRequestDirectory => Path.Combine(ReviewDirectory, "requests");
    public string CompletedReviewRequestDirectory => Path.Combine(ReviewRequestDirectory, "completed");
    public string ReviewReportsDirectory => Path.Combine(ReviewDirectory, "reports");
    public string ReviewFindingsDirectory => Path.Combine(ReviewDirectory, "findings");
    public string ReviewOpenFindingsDirectory => Path.Combine(ReviewFindingsDirectory, "open");
    public string ReviewClosedFindingsDirectory => Path.Combine(ReviewFindingsDirectory, "closed");
    public string WikiDirectory => Path.Combine(RootDirectory, "wiki");
    public string ResearchDirectory => Path.Combine(RootDirectory, "research");
    public string DenyListPath => Path.Combine(ResearchDirectory, "denylist.json");
    public string ResearchTopicsDirectory => Path.Combine(ResearchDirectory, "topics");
    public string CurationDirectory => Path.Combine(ResearchDirectory, "curation");
    public string CurationRequestsDirectory => Path.Combine(CurationDirectory, "requests");
    public string CurationFailedDirectory => Path.Combine(CurationDirectory, "failed");
    public string BatchesDirectory => Path.Combine(ResearchDirectory, "batches");
    public string PendingBatchesDirectory => Path.Combine(BatchesDirectory, "pending");
    public string AwaitingBatchesDirectory => Path.Combine(BatchesDirectory, "awaiting");
    public string CompletedBatchesDirectory => Path.Combine(BatchesDirectory, "completed");
    public string BlockedDomainsPath => Path.Combine(ResearchDirectory, "domains.json");
}
