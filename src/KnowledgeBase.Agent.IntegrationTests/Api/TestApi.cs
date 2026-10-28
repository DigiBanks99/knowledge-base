using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Common;
using KnowledgeBase.Agent.Research.Search;
using KnowledgeBase.Agent.Wiki;

using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

/// <summary>
/// The whole API, started from <c>Program</c> over a temporary wiki root. Only what needs the outside world is
/// replaced: Entra sign-in by <see cref="TestSignIn"/>, Redis by an in-memory output cache, every agent by a
/// <see cref="FakeAgent"/> and the web the search sources reach by <see cref="FakeWeb"/>. The rest (authorization, the CSRF check, routing, the watchers) runs as in production.
/// </summary>
internal sealed class TestApi : WebApplicationFactory<Program>
{
    /// <summary>The object id of the one user the API lets in.</summary>
    public const string OwnerObjectId = "6f1c8f0e-7f4a-4a52-9a43-0b0d2c1a9e11";

    private static readonly string[] KnownAgentNames =
        [KnownAgents.FactFinder, KnownAgents.ThreadWeaver, KnownAgents.Reviewer, KnownAgents.Editor, KnownAgents.Librarian, KnownAgents.Pleb];

    private readonly TemporaryDirectory _root = new();
    private readonly Dictionary<string, FakeAgent> _agents = KnownAgentNames.ToDictionary(name => name, name => new FakeAgent(name));

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public WikiConfigurationSettings Settings => Services.GetRequiredService<IOptions<WikiConfigurationSettings>>().Value;

    public FakeAgent Agent(string name) => _agents[name];

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>A client signed in as <paramref name="objectId"/>, or signed out when it is null.</summary>
    public HttpClient Client(string? objectId = OwnerObjectId, bool sendsCsrfHeader = true)
    {
        HttpClient client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (objectId is not null)
        {
            client.DefaultRequestHeaders.Add(TestSignIn.Header, objectId);
        }

        if (sendsCsrfHeader)
        {
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        }

        return client;
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Testing, not Development: Development would load the author's user secrets and personal instructions.
        builder.UseEnvironment("Testing");

        // Settings read while Program builds its services must be host settings; later configuration arrives too late.
        builder.UseSetting(ConfigFiles.ConfigDirectoryKey, Path.Combine(_root.Path, "no-config"));
        builder.UseSetting($"{WikiConfigurationSettings.SectionName}:{nameof(WikiConfigurationSettings.RootDirectory)}", _root.Path);
        builder.UseSetting("ConnectionStrings:cache", "localhost:6379,abortConnect=false");
        builder.UseSetting("Authentication:TenantId", "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("Authentication:ClientId", "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("Authentication:ClientSecret", "not-a-secret");
        builder.UseSetting("Authentication:AllowedObjectIds:0", OwnerObjectId);
        builder.UseSetting(AgentInstructions.SharedInstructionsKey, "Shared instructions.");
        builder.UseSetting(AgentInstructions.WikiRulesKey, "Wiki rules.");
        foreach (string agent in KnownAgentNames)
        {
            builder.UseSetting($"{agent}:Instructions", $"You are the {agent}.");
            builder.UseSetting($"{agent}:ApiKey", "not-a-key");
        }

        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOutputCacheStore>();
            services.AddOutputCache();

            services
                .AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestSignIn>(TestSignIn.SchemeName, null);
            // Requests are authenticated by the test scheme; challenges and forbids still go through the app's own
            // cookie and OpenID Connect handlers, so their 401 and 403 responses are the real ones.
            services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = TestSignIn.SchemeName);
            // The OpenID Connect handler would fetch Entra's metadata before its events run; a fixed configuration
            // lets the app's own challenge handling answer without the network.
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                OpenIdConnectConfiguration configuration = new()
                {
                    AuthorizationEndpoint = "https://login.invalid/authorize",
                    EndSessionEndpoint = "https://login.invalid/logout"
                };
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });

            foreach (FakeAgent agent in _agents.Values)
            {
                services.AddKeyedSingleton<AIAgent>(agent.Name, agent);
            }

            // The search sources (and their status signals) answer from the fake web instead of the internet.
            foreach (string client in new[] { SearxngSearchSource.HttpClientName, HackerNewsSearchSource.HttpClientName })
            {
                services.AddHttpClient(client).ConfigurePrimaryHttpMessageHandler(() => new FakeWeb.Handler(Web));
            }
        });
    }

    /// <summary>What the search sources' requests are answered with; by default an empty Hacker News search.</summary>
    public FakeWeb Web { get; } = new();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _root.Dispose();
        }
    }

    /// <summary>Signs a request in as the object id in its <see cref="Header"/>, or leaves it signed out.</summary>
    internal sealed class TestSignIn(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string Header = "X-Test-Object-Id";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Header, out Microsoft.Extensions.Primitives.StringValues objectId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            ClaimsIdentity identity = new(
                [new Claim("oid", objectId.ToString()), new Claim("name", "Test User")],
                SchemeName,
                "name",
                ClaimTypes.Role);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
