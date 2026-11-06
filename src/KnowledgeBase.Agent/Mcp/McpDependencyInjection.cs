using KnowledgeBase.Agent.Auth;

using Microsoft.AspNetCore.Authentication.JwtBearer;

using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace KnowledgeBase.Agent.Mcp;

/// <summary>
/// The connector Claude uses: a remote MCP server at <c>/mcp</c>. Claude signs the user in with Entra itself and sends
/// the access token as a bearer token, so the cookie session the UI uses plays no part here.
/// </summary>
internal static class McpDependencyInjection
{
    public const string Path = "/mcp";
    private const string McpUserPolicy = "McpUser";

    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddMcpConnector()
        {
            AuthSettings settings = builder.Configuration.GetSection(AuthSettings.SectionName).Get<AuthSettings>() ?? new AuthSettings();
            string authority = $"https://login.microsoftonline.com/{settings.TenantId}/v2.0";
            string scope = $"api://{settings.ClientId}/{settings.McpScope}";

            builder.Services
                .AddAuthentication()
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = authority;
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters.NameClaimType = "name";
                    // v2 tokens carry the client id as audience, v1 tokens the application id uri; the issuer
                    // differs the same way.
                    options.TokenValidationParameters.ValidAudiences = [settings.ClientId!, $"api://{settings.ClientId}"];
                    options.TokenValidationParameters.ValidIssuers =
                        [authority, $"https://sts.windows.net/{settings.TenantId}/"];
                })
                .AddMcp(options =>
                    options.Events.OnResourceMetadataRequest = context =>
                    {
                        HttpRequest request = context.HttpContext.Request;
                        context.ResourceMetadata = new ProtectedResourceMetadata
                        {
                            Resource = $"{request.Scheme}://{request.Host}{Path}",
                            AuthorizationServers = [authority],
                            ScopesSupported = [scope],
                            BearerMethodsSupported = ["header"]
                        };
                        return Task.CompletedTask;
                    });

            builder.Services.AddAuthorizationBuilder()
                .AddPolicy(McpUserPolicy, policy => policy
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, McpAuthenticationDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context => settings.IsAllowed(context.User)));

            builder.Services
                .AddMcpServer()
                .WithHttpTransport(options => options.Stateless = true)
                .WithTools<WikiTools>();

            return builder;
        }
    }

    extension(WebApplication app)
    {
        public WebApplication MapMcpConnector()
        {
            app.MapMcp(Path).RequireAuthorization(McpUserPolicy);
            return app;
        }
    }
}
