using System.Net;
using System.Net.Http.Json;

using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Agents.Commands;
using KnowledgeBase.Agent.Agents.Models;

using Microsoft.Extensions.Configuration;

namespace KnowledgeBase.Agent.IntegrationTests.Api;

public sealed class AgentsApiTests : IDisposable
{
    // The Pleb starts on the local model, so not every agent is on the same provider.
    private readonly TestApi _api = new(new Dictionary<string, string?>
    {
        [$"{OpenAICompatibleOptions.SectionName}:Endpoint"] = "http://127.0.0.1:9/v1",
        [$"{KnownAgents.Pleb}:Provider"] = nameof(ModelProvider.OpenAICompatible)
    });

    public void Dispose() => _api.Dispose();

    [Test]
    public async Task TheSettingsPageOpens()
    {
        HttpResponseMessage response = await _api.Client().GetAsync("/api/agents/providers");
        IReadOnlyList<AgentProviderSetting> settings = await TestApi.ReadAsync<IReadOnlyList<AgentProviderSetting>>(response);

        ItShouldSucceed();
        ItShouldListEveryAgentOnItsDefault();

        void ItShouldSucceed() => response.StatusCode.ShouldBe(HttpStatusCode.OK);

        void ItShouldListEveryAgentOnItsDefault() => settings
            .Select(setting => (setting.Agent, setting.Provider, setting.DefaultProvider))
            .ShouldBe(KnownAgents.All.Select(agent => agent == KnownAgents.Pleb
                ? (agent, ModelProvider.OpenAICompatible, ModelProvider.OpenAICompatible)
                : (agent, ModelProvider.Anthropic, ModelProvider.Anthropic)));
    }

    [Test]
    public async Task TheAuthorChoosesTheLocalModelWithoutAModelServer()
    {
        using TestApi api = new();

        HttpResponseMessage response = await api.Client().PutAsJsonAsync(
            "/api/agents/providers",
            new SetAgentProviderCommand(KnownAgents.Librarian, ModelProvider.OpenAICompatible),
            TestApi.Json);

        ItShouldBeAConflict();
        ItShouldLeaveTheAgentOnItsProvider();

        void ItShouldBeAConflict() => response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        void ItShouldLeaveTheAgentOnItsProvider() => api.Get<IConfiguration>()[$"{KnownAgents.Librarian}:Provider"]
            .ShouldBeNull();
    }

    [Test]
    public async Task TheAuthorPutsAnAgentOnTheLocalModel()
    {
        HttpResponseMessage response = await _api.Client().PutAsJsonAsync(
            "/api/agents/providers",
            new SetAgentProviderCommand(KnownAgents.Librarian, ModelProvider.OpenAICompatible),
            TestApi.Json);
        AgentProviderSetting setting = await TestApi.ReadAsync<AgentProviderSetting>(response);

        ItShouldReturnTheAgentOnItsNewProvider();
        ItShouldSwitchTheAgentAtOnce();
        ItShouldKeepTheChoiceForARestart();

        void ItShouldReturnTheAgentOnItsNewProvider() => setting.Provider.ShouldBe(ModelProvider.OpenAICompatible);

        void ItShouldSwitchTheAgentAtOnce() => _api.Get<IConfiguration>()[$"{KnownAgents.Librarian}:Provider"]
            .ShouldBe(nameof(ModelProvider.OpenAICompatible));

        void ItShouldKeepTheChoiceForARestart() => File.Exists(_api.Settings.AgentProvidersPath).ShouldBeTrue();
    }

    [Test]
    public async Task TheAppRestartsAfterAnAgentWasSwitched()
    {
        string path = _api.SettingsBeforeStart.AgentProvidersPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{ "Librarian": "OpenAICompatible" }""");

        IReadOnlyList<AgentProviderSetting> settings = await _api.Client()
            .GetFromJsonAsync<IReadOnlyList<AgentProviderSetting>>("/api/agents/providers", TestApi.Json) ?? [];

        ItShouldStartTheAgentOnTheChosenProvider();

        void ItShouldStartTheAgentOnTheChosenProvider() => settings
            .Single(setting => setting.Agent == KnownAgents.Librarian)
            .Provider.ShouldBe(ModelProvider.OpenAICompatible);
    }

    [Test]
    public async Task TheAuthorChoosesAProviderForAnUnknownAgent()
    {
        HttpResponseMessage response = await _api.Client().PutAsJsonAsync(
            "/api/agents/providers",
            new SetAgentProviderCommand("Scribe", ModelProvider.OpenAICompatible),
            TestApi.Json);

        ItShouldBeNotFound();

        void ItShouldBeNotFound() => response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TheAuthorSwitchesWithoutTheCsrfHeader()
    {
        HttpResponseMessage response = await _api.Client(sendsCsrfHeader: false).PutAsJsonAsync(
            "/api/agents/providers",
            new SetAgentProviderCommand(KnownAgents.Librarian, ModelProvider.OpenAICompatible),
            TestApi.Json);

        ItShouldBeRefused();

        void ItShouldBeRefused() => response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
