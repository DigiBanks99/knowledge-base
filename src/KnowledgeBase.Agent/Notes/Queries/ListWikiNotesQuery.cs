using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Notes.Queries;

public record ListWikiNotesQuery();

internal sealed class ListWikiNotesQueryHandler(
    ListFilesHandler listFilesHandler,
    IOptions<WikiConfigurationSettings> options)
{
    public Result<Notes.Models.Notes, Error> Handle(ListWikiNotesQuery _) =>
        GetNewFiles()
            .BindZip(AddProcessingFiles)
            .BindZip(AddProcessedFiles)
            .Map(files => new Notes.Models.Notes(files.First, files.Second, files.Third));

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