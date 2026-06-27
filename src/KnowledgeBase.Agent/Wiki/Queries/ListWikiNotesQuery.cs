using KnowledgeBase.Agent.Wiki.Files;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Wiki.Queries;

public record ListWikiNotesQuery();
public record Notes(IEnumerable<string> New, IEnumerable<string> Processing, IEnumerable<string> Processed);

internal sealed class ListWikiNotesQueryHandler(
    ListFilesHandler listFilesHandler,
    IOptions<WikiConfigurationSettings> options)
{
    public Result<Notes, Error> Handle(ListWikiNotesQuery _) =>
        GetNewFiles()
            .BindZip(AddProcessingFiles)
            .BindZip(AddProcessedFiles)
            .Map(files => new Notes(files.First, files.Second, files.Third));

    private Result<IEnumerable<string>, Error> GetNewFiles() => listFilesHandler
        .Handle(new ListFilesRequest(options.Value.NewNotesDirectory));

    private Result<IEnumerable<string>, Error> AddProcessingFiles(
        IEnumerable<string> newFiles) => listFilesHandler
            .Handle(new ListFilesRequest(options.Value.ProcessingNotesDirectory));

    private Result<IEnumerable<string>, Error> AddProcessedFiles(
        IEnumerable<string> newFiles,
        IEnumerable<string> processingFiles) => listFilesHandler
            .Handle(new ListFilesRequest(options.Value.ProcessedNotesDirectory));
}