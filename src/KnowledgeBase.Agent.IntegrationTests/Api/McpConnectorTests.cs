using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

/// <summary>The connector Claude uses: tools over MCP at <c>/mcp</c>, signed in with an Entra bearer token.</summary>
public sealed class McpConnectorTests : IDisposable
{
    private readonly TestApi _api = new();

    public void Dispose() => _api.Dispose();

    [Test]
    public async Task ClaudeConnectsWithoutSigningIn()
    {
        HttpResponseMessage response = await _api.CreateClient().PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "ping" });
        HttpResponseMessage metadata = await _api.CreateClient().GetAsync("/.well-known/oauth-protected-resource");
        JsonElement document = await metadata.Content.ReadFromJsonAsync<JsonElement>();

        ItShouldAskClaudeToSignIn();
        ItShouldSayWhereToFindTheSignInDetails();
        ItShouldNameThisServerAsTheAuthorizationServer();

        void ItShouldAskClaudeToSignIn() => response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        void ItShouldSayWhereToFindTheSignInDetails() =>
            response.Headers.WwwAuthenticate.ToString().ShouldContain("resource_metadata=");

        void ItShouldNameThisServerAsTheAuthorizationServer()
        {
            metadata.StatusCode.ShouldBe(HttpStatusCode.OK);
            document.GetProperty("authorization_servers")[0].GetString()
                .ShouldBe("http://localhost");
            document.GetProperty("scopes_supported")[0].GetString()
                .ShouldBe("api://00000000-0000-0000-0000-000000000002/access_as_user");
        }
    }

    [Test]
    public async Task SomeoneWhoIsNotAllowedInConnects()
    {
        Exception? refused = await Catch.ExceptionAsync(async () => await _api.ConnectMcpAsync("a-stranger"));

        ItShouldTurnThemAway();

        void ItShouldTurnThemAway() => refused.ShouldNotBeNull();
    }

    [Test]
    public async Task ATokenForAnotherAppConnects()
    {
        HttpClient http = _api.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", TestApi.BearerToken(TestApi.OwnerObjectId, audience: "api://someone-else"));

        HttpResponseMessage response = await http.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "ping" });

        ItShouldAskClaudeToSignInAgain();

        void ItShouldAskClaudeToSignInAgain() => response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ClaudeListsTheTools()
    {
        await using McpClient client = await _api.ConnectMcpAsync();

        IList<McpClientTool> tools = await client.ListToolsAsync();

        ItShouldOfferToCaptureANoteAndAskTheWiki();

        void ItShouldOfferToCaptureANoteAndAskTheWiki() =>
            tools.Select(tool => tool.Name).Order().ShouldBe(["ask_wiki", "capture_note"]);
    }

    [Test]
    public async Task ClaudeCapturesANote()
    {
        await using McpClient client = await _api.ConnectMcpAsync();

        CallToolResult result = await client.CallToolAsync("capture_note", new Dictionary<string, object?> { ["content"] = "Susan gave feedback." });
        bool processed = await Eventually.Until(() => _api.Agent(KnownAgents.FactFinder).Prompts.Count == 1);

        ItShouldCaptureIt();
        ItShouldStartProcessingIt();

        void ItShouldCaptureIt()
        {
            result.IsError.ShouldNotBe(true);
            result.Content.OfType<TextContentBlock>().Single().Text.ShouldMatch(@"^Captured as note-\d{4}-\d{2}-\d{2}-\d{6}\.md");
        }

        void ItShouldStartProcessingIt()
        {
            processed.ShouldBeTrue();
            _api.Agent(KnownAgents.FactFinder).Prompts.Single().ShouldContain("Susan gave feedback.");
        }
    }

    [Test]
    public async Task ClaudeCapturesAnEmptyNote()
    {
        await using McpClient client = await _api.ConnectMcpAsync();

        CallToolResult result = await client.CallToolAsync("capture_note", new Dictionary<string, object?> { ["content"] = "  " });

        ItShouldReportTheMistake();
        ItShouldNotStartProcessing();

        void ItShouldReportTheMistake() => result.IsError.ShouldBe(true);

        void ItShouldNotStartProcessing() => _api.Agent(KnownAgents.FactFinder).Prompts.ShouldBeEmpty();
    }

    [Test]
    public async Task ClaudeAsksAboutAConversationThatDoesNotExist()
    {
        await using McpClient client = await _api.ConnectMcpAsync();

        CallToolResult result = await client.CallToolAsync(
            "ask_wiki",
            new Dictionary<string, object?> { ["question"] = "Who is Susan?", ["conversationId"] = Guid.NewGuid().ToString("N") });

        ItShouldReportTheMistake();
        ItShouldNotAskTheLibrarian();

        void ItShouldReportTheMistake() => result.IsError.ShouldBe(true);

        void ItShouldNotAskTheLibrarian() => _api.Agent(KnownAgents.Librarian).Prompts.ShouldBeEmpty();
    }
}
