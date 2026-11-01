using System.Net.Http.Headers;

using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Research;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Status;

/// <summary>
/// Whether the OpenAI-compatible model server answers its health route. llama-server serves <c>/health</c> beside the
/// versioned API, answering 503 while it is still loading a model.
/// </summary>
internal sealed class ModelServerHealthCheck(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
    : IHealthCheck
{
    public const string Name = "Model server";
    public const string HttpClientName = "ModelServerHealth";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // The AppHost always passes an endpoint, so only a server some agent or the Keeper uses is worth checking.
        if (!KnownAgents.All.Any(UsesOpenAICompatibleProvider) && !UsesSharedServerForKeeper(configuration))
        {
            return HealthCheckResult.Healthy("Not used; neither an agent nor the Keeper uses the OpenAI-compatible server.");
        }

        OpenAICompatibleOptions server = configuration
            .GetSection(OpenAICompatibleOptions.SectionName)
            .Get<OpenAICompatibleOptions>() ?? new OpenAICompatibleOptions();
        if (server.Endpoint is null)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"Not configured; an agent uses the OpenAI-compatible provider but '{OpenAICompatibleOptions.SectionName}:Endpoint' is not set.");
        }

        Uri healthUri = HealthUri(server.Endpoint);
        using HttpRequestMessage request = new(HttpMethod.Get, healthUri);
        if (!string.IsNullOrWhiteSpace(server.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.ApiKey);
        }

        try
        {
            using HttpResponseMessage response = await httpClientFactory
                .CreateClient(HttpClientName)
                .SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"Reachable at {healthUri}.")
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"{healthUri} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (Exception ex) when (ex is (HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, $"Unreachable at {healthUri}: {ex.Message}");
        }
    }

    private bool UsesOpenAICompatibleProvider(string agent) => string.Equals(
        configuration[$"{agent}:{nameof(AgentModelOptions.Provider)}"],
        nameof(ModelProvider.OpenAICompatible),
        StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the research Keeper uses the shared server: it does while triage is on, unless a server of its own is
    /// configured. It is a decision model on <c>/v1/systemone</c>, not an agent, so no provider setting says so.
    /// </summary>
    public static bool UsesSharedServerForKeeper(IConfiguration configuration) =>
        configuration.GetValue($"{ResearchSettings.SectionName}:{nameof(ResearchSettings.TriageEnabled)}", true)
        && configuration.GetValue($"{ResearchSettings.SectionName}:{nameof(ResearchSettings.ScoutEnabled)}", true)
        && string.IsNullOrWhiteSpace(configuration[$"{ResearchSettings.SectionName}:{nameof(ResearchSettings.KeeperEndpoint)}"]);

    /// <summary>The server's health route: the endpoint with its <c>v1</c> version segment replaced by <c>health</c>.</summary>
    public static Uri HealthUri(Uri endpoint) => ServerUri(endpoint, "health");

    /// <summary>
    /// One of llama-server's own routes, which it serves beside the versioned API: the endpoint with its <c>v1</c>
    /// version segment replaced by <paramref name="route"/>.
    /// </summary>
    public static Uri ServerUri(Uri endpoint, string route)
    {
        string path = endpoint.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^"/v1".Length];
        }

        return new Uri(endpoint, $"{path}/{route}");
    }
}
