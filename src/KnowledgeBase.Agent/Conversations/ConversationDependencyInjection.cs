namespace KnowledgeBase.Agent.Conversations;

internal static class ConversationDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddConversations()
        {
            builder.Services.AddSingleton<ConversationService>();
            return builder;
        }
    }
}