using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using KnowledgeBase.Agent.Agents;

using Microsoft.Extensions.AI;

using OpenAI;

namespace KnowledgeBase.Agent.Tests.Agents;

public sealed class AgentConfigurationTests
{
    private readonly CapturingHandler _handler = new();
    private readonly IChatClient _chatClient;

    public AgentConfigurationTests()
    {
        OpenAIClient client = new(new ApiKeyCredential("unused"), new OpenAIClientOptions
        {
            Endpoint = new Uri("http://model-server.test/v1"),
            Transport = new HttpClientPipelineTransport(new HttpClient(_handler)),
        });
        _chatClient = client.GetChatClient("qwen").AsIChatClient();
    }

    [Test]
    public async Task AnAgentIsConfiguredWithoutThinking()
    {
        await AgentConfiguration.WithoutThinking(_chatClient).GetResponseAsync("Is this news?");

        ItShouldAskTheChatTemplateNotToThink();

        void ItShouldAskTheChatTemplateNotToThink() =>
            (_handler.Request?["chat_template_kwargs"]?["enable_thinking"]?.GetValue<bool>()).ShouldBe(false);
    }

    [Test]
    public async Task AnAgentIsConfiguredWithThinking()
    {
        await _chatClient.GetResponseAsync("Is this news?");

        ItShouldLeaveTheChatTemplateAlone();

        void ItShouldLeaveTheChatTemplateAlone() => _handler.Request!.AsObject().ContainsKey("chat_template_kwargs").ShouldBeFalse();
    }

    /// <summary>Answers every chat completion with a canned reply, keeping the last request body.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private const string Reply = """
            {"id":"chatcmpl-1","object":"chat.completion","created":0,"model":"qwen",
             "choices":[{"index":0,"message":{"role":"assistant","content":"Yes."},"finish_reason":"stop"}]}
            """;

        public JsonNode? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Reply, Encoding.UTF8, "application/json"),
            };
        }
    }
}
