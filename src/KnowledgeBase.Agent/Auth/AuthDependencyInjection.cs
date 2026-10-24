using System.Net;
using System.Security.Claims;

using KnowledgeBase.Agent.Wiki;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace KnowledgeBase.Agent.Auth;

public sealed record CurrentUser(string Name);

internal static class AuthDependencyInjection
{
    private const string AllowedUserPolicy = "AllowedUser";
    private const string CsrfHeader = "X-Requested-With";
    private const string ObjectIdClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    extension(WebApplicationBuilder builder)
    {
        /// <summary>
        /// Adds Entra ID sign-in. There is no way to run without it: missing settings stop the app.
        /// </summary>
        public WebApplicationBuilder AddAuth()
        {
            AuthSettings settings = builder.Configuration.GetSection(AuthSettings.SectionName).Get<AuthSettings>() ?? new AuthSettings();

            builder.AddForwardedHeaders();
            builder.AddDataProtectionKeys();

            if (!settings.IsConfigured)
            {
                throw new InvalidOperationException(
                    $"Sign-in is not configured. Set {AuthSettings.SectionName}:TenantId, ClientId and ClientSecret.");
            }

            builder.Services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                })
                .AddCookie(options =>
                {
                    options.Cookie.Name = "kb.session";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.ExpireTimeSpan = TimeSpan.FromDays(14);
                    options.SlidingExpiration = true;
                    options.Events.OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    };
                })
                .AddOpenIdConnect(options =>
                {
                    options.Authority = $"https://login.microsoftonline.com/{settings.TenantId}/v2.0";
                    options.ClientId = settings.ClientId;
                    options.ClientSecret = settings.ClientSecret;
                    options.ResponseType = "code";
                    options.UsePkce = true;
                    options.Scope.Clear();
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                    options.SaveTokens = false;
                    options.MapInboundClaims = true;
                    options.TokenValidationParameters.NameClaimType = "name";
                    options.Events.OnRedirectToIdentityProvider = context =>
                    {
                        // The API answers unauthenticated calls with 401 so the UI can send the user to sign in,
                        // rather than following a redirect to a sign-in page it cannot render.
                        if (context.Request.Path.StartsWithSegments("/api"))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.HandleResponse();
                        }

                        return Task.CompletedTask;
                    };
                });

            builder.Services.AddAuthorizationBuilder()
                .AddPolicy(AllowedUserPolicy, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context =>
                        settings.AllowedObjectIds.Length == 0
                        || settings.AllowedObjectIds.Contains(
                            context.User.FindFirstValue(ObjectIdClaim) ?? context.User.FindFirstValue("oid"),
                            StringComparer.OrdinalIgnoreCase)));

            return builder;
        }

        /// <summary>
        /// The keys that protect the session cookie are kept with the wiki, so sessions survive a restart or a new
        /// container. Otherwise every deployment would sign the user out.
        /// </summary>
        private void AddDataProtectionKeys()
        {
            WikiConfigurationSettings wiki = builder.Configuration.GetSection(WikiConfigurationSettings.SectionName).Get<WikiConfigurationSettings>()
                ?? throw new InvalidOperationException($"The '{WikiConfigurationSettings.SectionName}' configuration section is required.");

            builder.Services
                .AddDataProtection()
                .SetApplicationName("KnowledgeBase")
                .PersistKeysToFileSystem(new DirectoryInfo(wiki.KeysDirectory));
        }

        /// <summary>
        /// Behind Cloudflare (via cloudflared or a reverse proxy) the original scheme and host arrive in forwarded
        /// headers; they decide the sign-in redirect address. Only proxies on this machine are trusted, unless more are
        /// configured under ForwardedHeaders:KnownProxies.
        /// </summary>
        private void AddForwardedHeaders()
        {
            string[] knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
                foreach (string proxy in knownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }
            });
        }
    }

    extension(WebApplication app)
    {
        public WebApplication UseAuth()
        {
            app.UseForwardedHeaders();
            app.UseAuthentication();
            app.UseAuthorization();

            return app;
        }

        public WebApplication MapAuthEndpoints()
        {
            RouteGroupBuilder auth = app.MapGroup("auth").ExcludeFromDescription();

            auth.MapGet("login", (string? returnUrl) => Results.Challenge(
                new AuthenticationProperties { RedirectUri = IsLocalUrl(returnUrl) ? returnUrl! : "/" },
                [OpenIdConnectDefaults.AuthenticationScheme]));

            auth.MapGet("logout", () => Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

            return app;
        }
    }

    extension(RouteGroupBuilder api)
    {
        /// <summary>
        /// Requires a signed-in, allowed user for the whole API, and adds a check against cross-site request forgery:
        /// state-changing calls must carry a custom header, which other sites cannot send without the API's consent.
        /// </summary>
        public RouteGroupBuilder RequireSignedInUser()
        {
            api.RequireAuthorization(AllowedUserPolicy);

            api.AddEndpointFilter(async (context, next) =>
            {
                HttpRequest request = context.HttpContext.Request;
                bool changesState = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method);
                return changesState && !request.Headers.ContainsKey(CsrfHeader)
                    ? Results.Problem($"The {CsrfHeader} header is required.", statusCode: StatusCodes.Status400BadRequest)
                    : await next(context);
            });

            api.MapGet("me", (ClaimsPrincipal user) => new CurrentUser(user.Identity?.Name ?? "Signed in"))
                .WithTags("auth")
                .WithName("GetCurrentUser")
                .WithSummary("The signed-in user");

            return api;
        }
    }

    private static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && url.StartsWith('/')
        && !url.StartsWith("//", StringComparison.Ordinal)
        && !url.StartsWith("/\\", StringComparison.Ordinal);
}