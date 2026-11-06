using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Web;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

/// <summary>Claude's sign-in passes through the API, which drops the <c>resource</c> parameter Entra cannot handle.</summary>
public sealed class McpOAuthProxyTests : IDisposable
{
    private const string TenantAuthority = "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000001/oauth2/v2.0";

    private readonly TestApi _api = new();

    public void Dispose() => _api.Dispose();

    [Test]
    public async Task ClaudeFindsTheAuthorizationServer()
    {
        HttpResponseMessage response = await _api.CreateClient().GetAsync("/.well-known/oauth-authorization-server");
        System.Text.Json.JsonElement document = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        ItShouldPointToTheProxyEndpoints();
        ItShouldRequireProofKeysForCodeExchange();

        void ItShouldPointToTheProxyEndpoints()
        {
            document.GetProperty("authorization_endpoint").GetString().ShouldBe("http://localhost/oauth/authorize");
            document.GetProperty("token_endpoint").GetString().ShouldBe("http://localhost/oauth/token");
        }

        void ItShouldRequireProofKeysForCodeExchange() =>
            document.GetProperty("code_challenge_methods_supported")[0].GetString().ShouldBe("S256");
    }

    [Test]
    public async Task ClaudeStartsSigningIn()
    {
        const string Query = "response_type=code&client_id=abc&redirect_uri=https%3A%2F%2Fclaude.ai%2Fapi%2Fmcp%2Fauth_callback"
            + "&code_challenge=xyz&code_challenge_method=S256&state=s1&scope=api%3A%2F%2Fapi-id%2Faccess_as_user+offline_access"
            + "&resource=https%3A%2F%2Fknowledgebase.example%2Fmcp";

        HttpResponseMessage response = await _api.Client(objectId: null).GetAsync($"/oauth/authorize?{Query}");
        Uri location = response.Headers.Location!;
        System.Collections.Specialized.NameValueCollection forwarded = HttpUtility.ParseQueryString(location.Query);

        ItShouldSendThemToEntra();
        ItShouldKeepTheRestOfTheRequest();
        ItShouldDropTheResource();

        void ItShouldSendThemToEntra()
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            $"{location.GetLeftPart(UriPartial.Path)}".ShouldBe($"{TenantAuthority}/authorize");
        }

        void ItShouldKeepTheRestOfTheRequest()
        {
            forwarded["client_id"].ShouldBe("abc");
            forwarded["state"].ShouldBe("s1");
            forwarded["code_challenge"].ShouldBe("xyz");
            forwarded["scope"].ShouldBe("api://api-id/access_as_user offline_access");
            forwarded["redirect_uri"].ShouldBe("https://claude.ai/api/mcp/auth_callback");
        }

        void ItShouldDropTheResource() => forwarded["resource"].ShouldBeNull();
    }

    [Test]
    public async Task ClaudeExchangesTheCode()
    {
        string? forwardedBody = null;
        string? forwardedAuthorization = null;
        _api.Web.Respond = request =>
        {
            forwardedBody = new StreamReader(request.Content!.ReadAsStream()).ReadToEnd();
            forwardedAuthorization = request.Headers.Authorization?.ToString();
            return FakeWeb.Json("""{ "access_token": "token" }""");
        };
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = "the-code",
            ["client_id"] = "abc",
            ["code_verifier"] = "verifier",
            ["resource"] = "https://knowledgebase.example/mcp"
        });
        using HttpRequestMessage request = new(HttpMethod.Post, "/oauth/token") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("abc:secret")));

        HttpResponseMessage response = await _api.Client(objectId: null).SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        ItShouldAskEntraForTheToken();
        ItShouldDropTheResource();
        ItShouldPassOnTheClientsCredentials();
        ItShouldReturnEntrasAnswer();

        void ItShouldAskEntraForTheToken() =>
            _api.Web.Requests.Where(uri => uri.Host == "login.microsoftonline.com").Select(uri => uri.ToString()).ShouldBe([$"{TenantAuthority}/token"]);

        void ItShouldDropTheResource()
        {
            forwardedBody.ShouldNotBeNull().ShouldNotContain("resource");
            forwardedBody.ShouldContain("code=the-code");
            forwardedBody.ShouldContain("code_verifier=verifier");
        }

        void ItShouldPassOnTheClientsCredentials() => forwardedAuthorization.ShouldStartWith("Basic ");

        void ItShouldReturnEntrasAnswer()
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            body.ShouldContain("access_token");
        }
    }

    [Test]
    public async Task EntraRefusesTheCode()
    {
        _api.Web.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{ "error": "invalid_grant" }""", Encoding.UTF8, "application/json")
        };
        using FormUrlEncodedContent form = new(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = "bad" });

        HttpResponseMessage response = await _api.Client(objectId: null).PostAsync("/oauth/token", form);
        string body = await response.Content.ReadAsStringAsync();

        ItShouldPassOnTheRefusal();

        void ItShouldPassOnTheRefusal()
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            body.ShouldContain("invalid_grant");
        }
    }
}
