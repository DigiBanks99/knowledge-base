using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnowledgeBase.Agent.Common;

/// <summary>
/// Reads and writes JSON documents that are updated in place (runs, conversations). Writes go to a temporary file
/// that replaces the target, so readers never see a half-written document.
/// </summary>
internal static class JsonFileStore
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    public static async Task WriteAsync<T>(string filePath, T document, CancellationToken cancellationToken)
    {
        string temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken);
        }

        File.Move(temporaryPath, filePath, overwrite: true);
    }

    public static async Task<T?> ReadAsync<T>(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return default;
        }

        await using FileStream stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }

    public static async Task<IReadOnlyList<T>> ReadAllAsync<T>(string directory, CancellationToken cancellationToken)
    {
        List<T> documents = [];
        foreach (string filePath in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                T? document = await ReadAsync<T>(filePath, cancellationToken);
                if (document is not null)
                {
                    documents.Add(document);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A document that is being replaced or is corrupt is skipped rather than failing the whole listing.
            }
        }

        return documents;
    }
}
