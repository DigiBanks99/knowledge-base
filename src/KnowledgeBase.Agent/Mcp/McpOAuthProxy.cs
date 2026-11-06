using System.Net.Http.Headers;

using KnowledgeBase.Agent.Auth;

using Microsoft.AspNetCore.Http.Extensions;

namespace KnowledgeBase.Agent.Mcp;

/// <summary>
/// Stands between Claude and Entra for the connector's sign-in. Claude follows the MCP specification and adds the
/// RFC 8707 <c>resource</c> parameter (the MCP URL) to its authorize and token requests, but Entra's v2 endpoints read
/// it as an API identifier and fail with AADSTS500011 because no API has that name. The proxy forwards everything else
/// unchanged, so the client id, secret, scope and PKCE flow still run between Claude and Entra.
/// </summary>
internal static class McpOAuthProxy
{
    public const string HttpClientName = "entra-oauth";
    private const string ResourceParameter = "resource";

    /// <summary>The address Claude knows this server by, rebuilt from the request since it sits behind a proxy.</summary>
    public static string BaseUrl(HttpRequest request) => $"{request.Scheme}://{request.Host}";

    extension(WebApplication app)
    {
        public WebApplication MapMcpOAuthProxy()
        {
            AuthSettings settings = app.Configuration.GetSection(AuthSettings.SectionName).Get<AuthSettings>() ?? new AuthSettings();
            string entra = $"https://login.microsoftonline.com/{settings.TenantId}/oauth2/v2.0";
            string scope = settings.McpScopeUri;

            app.MapGet("/.well-known/oauth-authorization-server", (HttpRequest request) =>
            {
                string baseUrl = BaseUrl(request);
                return Results.Json(new
                {
                    issuer = baseUrl,
                    authorization_endpoint = $"{baseUrl}/oauth/authorize",
                    token_endpoint = $"{baseUrl}/oauth/token",
                    response_types_supported = new[] { "code" },
                    grant_types_supported = new[] { "authorization_code", "refresh_token" },
                    code_challenge_methods_supported = new[] { "S256" },
                    token_endpoint_auth_methods_supported = new[] { "client_secret_post", "client_secret_basic" },
                    scopes_supported = new[] { scope, "offline_access" }
                });
            }).ExcludeFromDescription();

            app.MapGet("/oauth/authorize", (HttpRequest request) =>
            {
                QueryBuilder query = new(request.Query
                    .Where(parameter => parameter.Key != ResourceParameter)
                    .SelectMany(parameter => parameter.Value.Select(value => KeyValuePair.Create(parameter.Key, value!))));
                return Results.Redirect($"{entra}/authorize{query}");
            }).ExcludeFromDescription();

            app.MapPost("/oauth/token", async (
                HttpRequest request,
                IHttpClientFactory httpClients,
                CancellationToken cancellationToken) =>
            {
                if (!request.HasFormContentType)
                {
                    return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
                }

                IFormCollection form = await request.ReadFormAsync(cancellationToken);
                using HttpRequestMessage forwarded = new(HttpMethod.Post, $"{entra}/token")
                {
                    Content = new FormUrlEncodedContent(form
                        .Where(field => field.Key != ResourceParameter)
                        .SelectMany(field => field.Value.Select(value => KeyValuePair.Create(field.Key, value))))
                };

                // client_secret_basic: the client authenticates in the header rather than the form.
                if (AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out AuthenticationHeaderValue? authorization))
                {
                    forwarded.Headers.Authorization = authorization;
                }

                using HttpResponseMessage response = await httpClients.CreateClient(HttpClientName).SendAsync(forwarded, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
            }).ExcludeFromDescription();

            return app;
        }
    }
}
