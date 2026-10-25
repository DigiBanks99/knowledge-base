using System.Text.Json;

using KnowledgeBase.Agent.Facts.Models;
using KnowledgeBase.Agent.Files;
using KnowledgeBase.Agent.Wiki.Errors;

namespace KnowledgeBase.Agent.Facts.Queries;

public record ReadFactsQuery(string FactsPath);

internal sealed class ReadFactsQueryHandler(ReadFileHandler readFileHandler)
{
    public async Task<Result<FactContainer, Error>> Handle(
        ReadFactsQuery request,
        CancellationToken cancellationToken) => await readFileHandler
            .Handle(new ReadFileRequest(request.FactsPath), cancellationToken)
            .Bind(ReadFactsFromContent);

    private Result<FactContainer, Error> ReadFactsFromContent(string content) => Result.Try<FactContainer, Error>(
            () => JsonSerializer.Deserialize<FactContainer>(content, JsonSerializerOptions.Web)
                ?? throw new JsonException("Deserialized object is null."),
            error => new SerializationError(content, typeof(FactContainer)));
}
