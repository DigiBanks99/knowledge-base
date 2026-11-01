using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Status;

namespace KnowledgeBase.Agent.Research.Curation;

/// <summary>Counts the tokens of a prompt for the model it will be sent to, so a prompt over budget is refused.</summary>
internal interface IPromptTokenCounter
{
    Task<Result<int, Error>> CountAsync(string prompt, CancellationToken cancellationToken);
}

/// <summary>The model server could not count a prompt, so whether it fits is unknown; the work should wait.</summary>
internal sealed class TokenCountUnavailableError(string reason)
    : Error("TokenCountUnavailable", $"The model server could not count the prompt's tokens: {reason}");

/// <summary>
/// Asks llama-server's <c>/tokenize</c> route, which tokenizes with the model it has loaded, so the count is exact
/// whatever model is configured. The model is named because a router-mode server refuses to tokenize without one.
/// </summary>
internal sealed class LlamaServerTokenCounter(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : IPromptTokenCounter
{
    public const string HttpClientName = "ModelServerTokenize";

    public async Task<Result<int, Error>> CountAsync(string prompt, CancellationToken cancellationToken)
    {
        OpenAICompatibleOptions server = configuration
            .GetSection(OpenAICompatibleOptions.SectionName)
            .Get<OpenAICompatibleOptions>() ?? new OpenAICompatibleOptions();
        if (server.Endpoint is null)
        {
            return new TokenCountUnavailableError($"'{OpenAICompatibleOptions.SectionName}:Endpoint' is not set.");
        }

        Uri tokenizeUri = ModelServerHealthCheck.ServerUri(server.Endpoint, "tokenize");
        using HttpRequestMessage request = new(HttpMethod.Post, tokenizeUri)
        {
            Content = JsonContent.Create(new { content = prompt, model = server.Model })
        };
        if (!string.IsNullOrWhiteSpace(server.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.ApiKey);
        }

        try
        {
            using HttpResponseMessage response = await httpClientFactory
                .CreateClient(HttpClientName)
                .SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new TokenCountUnavailableError($"{tokenizeUri} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            using JsonDocument body = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            return body.RootElement.TryGetProperty("tokens", out JsonElement tokens) && tokens.ValueKind == JsonValueKind.Array
                ? tokens.GetArrayLength()
                : new TokenCountUnavailableError($"{tokenizeUri} answered without a `tokens` array.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new TokenCountUnavailableError($"{tokenizeUri}: {ex.Message}");
        }
    }
}

/// <summary>
/// Counts with llama-server while a research agent (the Pleb or the Clerk) runs on it, and estimates otherwise: only
/// llama-server can count tokens for the model it has loaded, and another provider's context is far larger than a
/// curation prompt. Decided per count, as an agent's provider can be switched while the app runs.
/// </summary>
internal sealed class ResearchTokenCounter(
    IConfiguration configuration,
    LlamaServerTokenCounter llamaServer,
    EstimatedTokenCounter estimate)
    : IPromptTokenCounter
{
    private static readonly string[] ResearchAgents = [KnownAgents.Pleb, KnownAgents.Clerk];

    public Task<Result<int, Error>> CountAsync(string prompt, CancellationToken cancellationToken) =>
        ResearchAgents.Any(agent => AgentProviders.Current(configuration, agent) == ModelProvider.OpenAICompatible)
            ? llamaServer.CountAsync(prompt, cancellationToken)
            : estimate.CountAsync(prompt, cancellationToken);
}

/// <summary>
/// A conservative estimate for a model with no tokenize route (an Anthropic model, whose context is far larger than
/// any curation prompt): English averages about four characters a token, so three over-counts rather than under.
/// </summary>
internal sealed class EstimatedTokenCounter : IPromptTokenCounter
{
    private const int CharactersPerToken = 3;

    public Task<Result<int, Error>> CountAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<int, Error>((prompt.Length + CharactersPerToken - 1) / CharactersPerToken));
}
