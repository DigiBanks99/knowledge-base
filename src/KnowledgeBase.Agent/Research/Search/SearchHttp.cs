using System.Text.Json;

namespace KnowledgeBase.Agent.Research.Search;

/// <summary>Fetches a search source's JSON answer, turning every way it can fail into a <see cref="SearchSourceError"/>.</summary>
internal static class SearchHttp
{
    public static async Task<Result<JsonDocument, Error>> GetJsonAsync(
        HttpClient client,
        Uri uri,
        string source,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await client.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new SearchSourceError(source, $"{uri.GetLeftPart(UriPartial.Path)} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            return await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new SearchSourceError(source, $"{uri.GetLeftPart(UriPartial.Path)}: {ex.Message}");
        }
    }

    /// <summary>
    /// <paramref name="endpoint"/>'s <paramref name="route"/>. The endpoint is made a base first, so one configured
    /// without a trailing slash (<c>https://hn.algolia.com/api/v1</c>) keeps its last segment.
    /// </summary>
    public static Uri Route(Uri endpoint, string route) =>
        new(endpoint.AbsolutePath.EndsWith('/') ? endpoint : new Uri($"{endpoint.GetLeftPart(UriPartial.Path)}/"), route);

    public static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
